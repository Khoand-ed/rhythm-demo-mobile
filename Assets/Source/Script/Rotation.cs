using UnityEngine;

public class ConstantSpinner2D : MonoBehaviour
{
    [Tooltip("Rotation speed in degrees per second.")]
    public float rotationSpeed = 90f;

    void Update()
    {
        // Vector3.forward is shorthand for (0, 0, 1), which is the Z-axis in 2D
        transform.Rotate(Vector3.forward * rotationSpeed * Time.deltaTime);
    }
}