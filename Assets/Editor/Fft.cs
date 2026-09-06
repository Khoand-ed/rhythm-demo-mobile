using UnityEngine;

// Iterative radix-2 Cooley-Tukey FFT over split real/imaginary arrays.
//
// Split float arrays rather than System.Numerics.Complex: Complex is a struct
// with no in-place mutation, so a Complex[] transform allocates per butterfly
// and runs measurably slower. The project also forbids unsafe code, so this is
// plain indexed access throughout.
//
// One instance is reused across every frame of a track: the bit-reversal table
// and twiddle factors are computed once in the constructor.
public class Fft
{
    private readonly int size;
    private readonly int[] reversed;
    private readonly float[] cosTable;
    private readonly float[] sinTable;

    public int Size
    {
        get { return size; }
    }

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new System.ArgumentException($"FFT size must be a power of two, got {size}.", "size");
        }

        this.size = size;

        int bits = 0;
        while ((1 << bits) < size) bits++;

        reversed = new int[size];
        for (int i = 0; i < size; i++)
        {
            int value = i;
            int result = 0;

            for (int bit = 0; bit < bits; bit++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }

            reversed[i] = result;
        }

        // Half a period is all the butterflies ever need.
        cosTable = new float[size / 2];
        sinTable = new float[size / 2];

        for (int i = 0; i < size / 2; i++)
        {
            float angle = -2f * Mathf.PI * i / size;
            cosTable[i] = Mathf.Cos(angle);
            sinTable[i] = Mathf.Sin(angle);
        }
    }

    // Transforms in place. Both arrays must be `Size` long; `im` is normally
    // all zeros on the way in for real input.
    public void Transform(float[] re, float[] im)
    {
        for (int i = 0; i < size; i++)
        {
            int j = reversed[i];

            if (j > i)
            {
                float tempRe = re[i];
                re[i] = re[j];
                re[j] = tempRe;

                float tempIm = im[i];
                im[i] = im[j];
                im[j] = tempIm;
            }
        }

        for (int span = 2; span <= size; span <<= 1)
        {
            int half = span >> 1;
            int step = size / span;

            for (int start = 0; start < size; start += span)
            {
                for (int offset = 0; offset < half; offset++)
                {
                    int twiddle = offset * step;
                    float wRe = cosTable[twiddle];
                    float wIm = sinTable[twiddle];

                    int a = start + offset;
                    int b = a + half;

                    float bRe = re[b] * wRe - im[b] * wIm;
                    float bIm = re[b] * wIm + im[b] * wRe;

                    re[b] = re[a] - bRe;
                    im[b] = im[a] - bIm;
                    re[a] += bRe;
                    im[a] += bIm;
                }
            }
        }
    }
}
