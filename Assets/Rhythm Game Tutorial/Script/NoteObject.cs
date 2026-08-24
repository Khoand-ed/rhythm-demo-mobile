using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteObject : MonoBehaviour
{

    public bool canBePressed;

    public KeyCode keyToPress;

    private bool Obtained = false;

    public GameObject hitEffect, goodEffect, perfectEffect, missEffect;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(keyToPress))
        {
            if (canBePressed)
            {
                Obtained = true;
                gameObject.SetActive(false);

                //GameManager.instance.NoteHit();
                if (transform.position.x >= 1.45 && transform.position.x <= 1.55)
                {
                    Debug.Log("Perfect");
                    GameManager.instance.PerfectHit();
                    Instantiate(perfectEffect, transform.position, perfectEffect.transform.rotation);
                } else if (transform.position.x >= -1.55 && transform.position.x <= -1.45)
                {
                    Debug.Log("Perfect");
                    GameManager.instance.PerfectHit();
                    Instantiate(perfectEffect, transform.position, perfectEffect.transform.rotation);
                }
                    else if (transform.position.x >= 1.25 && transform.position.x <= 1.75)
                    {
                        Debug.Log("Good");
                        GameManager.instance.GoodHit();
                        Instantiate(goodEffect, transform.position, goodEffect.transform.rotation);
                    }
                else if(transform.position.x >= -1.75 && transform.position.x <= -1.25)
                        {
                            Debug.Log("Good");
                            GameManager.instance.GoodHit();
                            Instantiate(goodEffect, transform.position, goodEffect.transform.rotation);
                    } else
                            {
                                Debug.Log("Hit");
                                GameManager.instance.NormalHit();
                                Instantiate(hitEffect, transform.position, hitEffect.transform.rotation);
                            }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if(other.tag == "Activator")
        {
            canBePressed = true;
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.tag == "Activator")
        {
            canBePressed = false;

            if (!Obtained)
            {
                GameManager.instance.NoteMissed();
                Instantiate(missEffect, transform.position, missEffect.transform.rotation);
            }
        }
    }
}
