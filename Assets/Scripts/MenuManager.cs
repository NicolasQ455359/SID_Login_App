using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public void Logout()
    {
        AuthManager.Instance.Logout();
    }
}