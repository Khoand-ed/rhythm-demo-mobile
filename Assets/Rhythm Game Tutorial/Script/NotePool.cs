using System.Collections.Generic;
using UnityEngine;

// Notes are recycled rather than instantiated per spawn, so a dense chart does
// not produce a GC spike mid-song on mobile.
public class NotePool : MonoBehaviour
{
    public NoteView notePrefab;

    public int prewarm = 16;

    private readonly Stack<NoteView> idle = new Stack<NoteView>();

    void Awake()
    {
        for (int i = 0; i < prewarm; i++)
        {
            idle.Push(CreateOne());
        }
    }

    private NoteView CreateOne()
    {
        NoteView note = Instantiate(notePrefab, transform);
        note.gameObject.SetActive(false);
        return note;
    }

    public NoteView Get()
    {
        NoteView note = idle.Count > 0 ? idle.Pop() : CreateOne();
        note.gameObject.SetActive(true);
        return note;
    }

    public void Release(NoteView note)
    {
        note.gameObject.SetActive(false);
        idle.Push(note);
    }
}
