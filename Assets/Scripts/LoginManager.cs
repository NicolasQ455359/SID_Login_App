using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class LoginManager : MonoBehaviour
{
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;

    string url = "https://sid-restapi.onrender.com/api/auth/login";

    [System.Serializable]
    public class LoginData
    {
        public string username;
        public string password;
    }

    public void OnLoginButton()
    {
        Debug.Log("CLICK LOGIN");
        StartCoroutine(Login());
    }

    IEnumerator Login()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;

        // Crear objeto con los datos
        LoginData data = new LoginData();
        data.username = username;
        data.password = password;

        // Convertir a JSON
        string json = JsonUtility.ToJson(data);
        Debug.Log("JSON enviado: " + json);

        // Crear request
        UnityWebRequest request = new UnityWebRequest(url, "POST");

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        // Respuesta
        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Respuesta: " + request.downloadHandler.text);

            // Guardar token (por ahora guardamos todo el JSON)
            PlayerPrefs.SetString("token", request.downloadHandler.text);
        }
        else
        {
            Debug.Log("Error: " + request.error);
            Debug.Log("Respuesta servidor: " + request.downloadHandler.text);
        }
    }
}