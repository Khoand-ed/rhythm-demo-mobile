using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BeatScroller : MonoBehaviour
{
    public float beatTempo;

    public bool hasStarted;

    public bool IsLeft;

    // Runs in Awake (not Start) so NoteObject.Start() can safely read the
    // normalized speed regardless of script execution order.
    void Awake()
    {
        beatTempo = beatTempo / 60f;
    }

    // Update is called once per frame
    void Update()
    {
        if(!hasStarted)
        {
            /*if (Input.anyKeyDown)
            {
                hasStarted = true;
            }*/
        } 
        else
        {
            if(IsLeft)
                transform.position += new Vector3(beatTempo * Time.deltaTime, 0f , 0f);
            else
                transform.position -= new Vector3(beatTempo * Time.deltaTime, 0f, 0f);
        }
    }
}
