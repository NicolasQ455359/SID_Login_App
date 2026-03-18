using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject loginPanel;
    public GameObject registerPanel;

    private void Start()
    {
        ShowLogin();
    }

    public void ShowLogin()
    {
        if (loginPanel != null)
        {
            loginPanel.SetActive(true);
        }

        if (registerPanel != null)
        {
            registerPanel.SetActive(false);
        }
    }

    public void ShowRegister()
    {
        if (loginPanel != null)
        {
            loginPanel.SetActive(false);
        }

        if (registerPanel != null)
        {
            registerPanel.SetActive(true);
        }
    }
}
