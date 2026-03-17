using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System.Text;
using System.Collections;
using UnityEngine.SceneManagement;

public class AuthManager : MonoBehaviour
{
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;

    public static AuthManager Instance;

    public string token;
    public string username;

    void Awake()
    {
        // Singleton
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ?? LOGIN
    public void Login()
    {
        Debug.Log("CLICK LOGIN");

        string username = usernameInput.text;
        string password = passwordInput.text;

        StartCoroutine(LoginRequest(username, password));
    }

    IEnumerator LoginRequest(string username, string password)
    {
        string url = "https://sid-restapi.onrender.com/api/auth/login";

        string json = JsonUtility.ToJson(new LoginData(username, password));

        Debug.Log("JSON enviado: " + json);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.Log("Error: " + request.error);
        }
        else
        {
            Debug.Log("Respuesta: " + request.downloadHandler.text);

            // ?? GUARDAR DATOS
            this.username = username;

            // ?? CAMBIO DE ESCENA AQUÍ
            SceneManager.LoadScene("Menu");
        }
    }

    // ?? LOGOUT
    public void Logout()
    {
        token = "";
        username = "";

        SceneManager.LoadScene("SampleScene");
    }
}

// Clase para enviar JSON
[System.Serializable]
public class LoginData
{
    public string username;
    public string password;

    public LoginData(string username, string password)
    {
        this.username = username;
        this.password = password;
    }
}