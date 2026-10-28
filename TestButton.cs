using UnityEngine;
using UnityEngine.InputSystem.Android;
using UnityEngine.UI;

public class TestButton : MonoBehaviour
{
    public GameObject selfbutton;
    public GameObject[] ButtonsToHide;
    public GameObject[] ButtonToShow;
    public void OnMainButtonClick()
    {
        if(selfbutton != null)
        {
            selfbutton.SetActive(false);
        }
        foreach(GameObject btn in ButtonsToHide)
        {
            if(btn != null)
            {
                btn.SetActive(false);
            }
        }
        foreach(GameObject btn in ButtonToShow)
        {
            if(btn != null)
            {
                btn.SetActive(true);
            }
        }
    }
    void Start()
    {
        
    }

    
    void Update()
    {
        
    }
}
