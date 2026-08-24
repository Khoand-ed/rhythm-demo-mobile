using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BeatScroller : MonoBehaviour
{
    public float beatTempo;

    public bool hasStarted;

    public bool IsLeft;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
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
