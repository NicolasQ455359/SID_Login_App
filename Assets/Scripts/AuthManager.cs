using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class AuthManager : MonoBehaviour
{
    [Header("Project")]
    [SerializeField] private string projectDisplayName = "SID Login App - Nicolas Quintero";
    [SerializeField] private string authenticationSceneName = "SampleScene";
    [SerializeField] private string menuSceneName = "Menu";

    [Header("API")]
    [SerializeField] private string apiBaseUrl = "https://sid-restapi.onrender.com";

    [Header("Optional UI References")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject registerPanel;
    [SerializeField] private TMP_InputField loginUsernameInput;
    [SerializeField] private TMP_InputField loginPasswordInput;
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_InputField registerPasswordInput;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text projectTitleLabel;

    public static AuthManager Instance;

    public string token;
    public string username;
    public string userId;

    private const string TokenKey = "sid_auth_token";
    private const string UsernameKey = "sid_auth_username";
    private const string UserIdKey = "sid_auth_user_id";

    private static readonly string[] LoginPaths =
    {
        "/api/auth/login"
    };

    private static readonly string[] RegisterPaths =
    {
        "/api/usuarios"
    };

    private bool isBusy;
    private bool isLoadingScene;

    public string ApiBaseUrl => apiBaseUrl.TrimEnd('/');
    public string ProjectDisplayName => projectDisplayName;
    public string MenuSceneName => menuSceneName;
    public string AuthenticationSceneName => authenticationSceneName;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSession();
        }
        else if (Instance != this)
        {
            // PROXY MODE: No se destruye el objeto para que los botones de la UI (configurados en el Inspector) no queden muertos.
            // Morirá naturalmente al cambiar de escena porque no tiene DontDestroyOnLoad.
        }
    }

    private void OnEnable()
    {
        if (Instance == this) SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (Instance == this) HandleScene(SceneManager.GetActiveScene());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleScene(scene);
    }

    private void HandleScene(Scene scene)
    {
        isLoadingScene = false;

        if (scene.name == authenticationSceneName)
        {
            CacheAuthSceneReferences();
            EnsureProjectTitle();

            if (IsAuthenticated())
            {
                SetStatus("Sesion detectada. Redirigiendo al menu...", false);
                LoadMenuScene();
            }
            else
            {
                SetStatus("Ingresa tus credenciales o crea una cuenta.", false);
            }
        }
        else if (scene.name == menuSceneName && !IsAuthenticated())
        {
            LoadAuthenticationScene();
        }
    }

    public void Login()
    {
        if (Instance != null && Instance != this)
        {
            Instance.Login();
            return;
        }

        CacheAuthSceneReferences();

        if (isBusy)
        {
            return;
        }

        if (loginUsernameInput == null || loginPasswordInput == null)
        {
            SetStatus("No se encontraron los campos de login en la escena.", true);
            return;
        }

        string loginUser = loginUsernameInput.text.Trim();
        string loginPassword = loginPasswordInput.text;

        if (string.IsNullOrWhiteSpace(loginUser) || string.IsNullOrWhiteSpace(loginPassword))
        {
            SetStatus("Debes completar usuario y contrasena para iniciar sesion.", true);
            return;
        }

        StartCoroutine(LoginRoutine(loginUser, loginPassword));
    }

    public void RegisterUser()
    {
        if (Instance != null && Instance != this)
        {
            Instance.RegisterUser();
            return;
        }

        CacheAuthSceneReferences();

        if (isBusy)
        {
            return;
        }

        if (registerUsernameInput == null || registerPasswordInput == null)
        {
            SetStatus("No se encontraron los campos de registro en la escena.", true);
            return;
        }

        string registerUser = registerUsernameInput.text.Trim();
        string registerPassword = registerPasswordInput.text;

        if (string.IsNullOrWhiteSpace(registerUser) || string.IsNullOrWhiteSpace(registerPassword))
        {
            SetStatus("Debes completar usuario y contrasena para registrarte.", true);
            return;
        }

        StartCoroutine(RegisterRoutine(registerUser, registerPassword));
    }

    public void Logout()
    {
        if (Instance != null && Instance != this)
        {
            Instance.Logout();
            return;
        }

        ClearSession();
        SetStatus("Sesion cerrada.", false);
        LoadAuthenticationScene();
    }

    public bool IsAuthenticated()
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (IsTokenExpired(token))
        {
            ClearSession();
            return false;
        }

        return true;
    }

    public void ApplyAuthorizationHeader(UnityWebRequest request)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.SetRequestHeader("x-token", token);
            request.SetRequestHeader("Authorization", "Bearer " + token);
        }
    }

    private IEnumerator LoginRoutine(string loginUser, string loginPassword)
    {
        isBusy = true;
        SetStatus("Iniciando sesion...", false);

        string payload = MiniJson.Json.Serialize(new Dictionary<string, object>
        {
            { "username", loginUser },
            { "password", loginPassword }
        });

        RequestResponse response = null;

        foreach (string path in LoginPaths)
        {
            yield return SendJsonRequest(ApiBaseUrl + path, UnityWebRequest.kHttpVerbPOST, payload, false, result => response = result);

            if (response == null)
            {
                continue;
            }

            if (response.StatusCode == 404)
            {
                continue;
            }

            break;
        }

        isBusy = false;

        if (response == null)
        {
            SetStatus("No fue posible contactar la API de login.", true);
            yield break;
        }

        if (!response.Success)
        {
            SetStatus(BuildErrorMessage(response, "No fue posible iniciar sesion."), true);
            yield break;
        }

        string receivedToken = ExtractToken(response);
        if (string.IsNullOrWhiteSpace(receivedToken))
        {
            SetStatus("El login respondio correctamente, pero no devolvio un token reconocible.", true);
            yield break;
        }

        string resolvedUsername = ExtractStringValue(response.Body, "username", "userName", "email", "correo");
        if (string.IsNullOrWhiteSpace(resolvedUsername))
        {
            resolvedUsername = loginUser;
        }

        string resolvedUserId = ExtractStringValue(response.Body, "_id", "uid", "id");
        SaveSession(receivedToken, resolvedUsername, resolvedUserId);
        SetStatus("Sesion iniciada correctamente.", false);
        LoadMenuScene();
    }

    private IEnumerator RegisterRoutine(string registerUser, string registerPassword)
    {
        isBusy = true;
        SetStatus("Registrando usuario...", false);

        string payload = MiniJson.Json.Serialize(new Dictionary<string, object>
        {
            { "username", registerUser },
            { "password", registerPassword }
        });

        RequestResponse response = null;

        foreach (string path in RegisterPaths)
        {
            yield return SendJsonRequest(ApiBaseUrl + path, UnityWebRequest.kHttpVerbPOST, payload, false, result => response = result);

            if (response == null)
            {
                continue;
            }

            if (response.StatusCode == 404)
            {
                continue;
            }

            break;
        }

        isBusy = false;

        if (response == null)
        {
            SetStatus("No fue posible contactar la API de registro.", true);
            yield break;
        }

        if (!response.Success)
        {
            SetStatus(BuildErrorMessage(response, "No fue posible registrar el usuario."), true);
            yield break;
        }

        SetStatus("Usuario registrado correctamente. Ahora puedes iniciar sesion.", false);

        if (registerUsernameInput != null)
        {
            registerUsernameInput.text = string.Empty;
        }

        if (registerPasswordInput != null)
        {
            registerPasswordInput.text = string.Empty;
        }

        if (loginUsernameInput != null)
        {
            loginUsernameInput.text = registerUser;
        }

        UIManager uiManager = FindAnyObjectByType<UIManager>();
        if (uiManager != null)
        {
            uiManager.ShowLogin();
        }
    }

    private IEnumerator SendJsonRequest(string url, string method, string jsonPayload, bool includeAuth, Action<RequestResponse> onComplete)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, method))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload ?? string.Empty);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            if (includeAuth)
            {
                ApplyAuthorizationHeader(request);
            }

            yield return request.SendWebRequest();

            RequestResponse response = new RequestResponse
            {
                Url = url,
                StatusCode = request.responseCode,
                Success = request.result == UnityWebRequest.Result.Success,
                Body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                Error = request.error,
                Headers = request.GetResponseHeaders()
            };

            onComplete?.Invoke(response);
        }
    }

    private string BuildErrorMessage(RequestResponse response, string fallback)
    {
        string apiMessage = ExtractStringValue(response.Body, "msg", "message", "error", "detail");
        if (!string.IsNullOrWhiteSpace(apiMessage))
        {
            return apiMessage;
        }

        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            return response.Error;
        }

        return fallback;
    }

    private string ExtractToken(RequestResponse response)
    {
        string parsedToken = ExtractStringValue(response.Body, "token", "accessToken", "access_token", "jwt");
        if (!string.IsNullOrWhiteSpace(parsedToken))
        {
            return parsedToken;
        }

        if (response.Headers != null)
        {
            if (response.Headers.TryGetValue("Authorization", out string authHeader))
            {
                return authHeader.Replace("Bearer ", string.Empty).Trim();
            }

            if (response.Headers.TryGetValue("x-auth-token", out string headerToken))
            {
                return headerToken.Trim();
            }
        }

        return string.Empty;
    }

    private string ExtractStringValue(string json, params string[] candidateKeys)
    {
        object parsed = null;
        try
        {
            parsed = MiniJson.Json.Deserialize(json);
        }
        catch
        {
            parsed = null;
        }

        object resolved = FindValueRecursive(parsed, candidateKeys);
        return resolved != null ? resolved.ToString() : string.Empty;
    }

    private object FindValueRecursive(object node, string[] candidateKeys)
    {
        if (node is Dictionary<string, object> dictionary)
        {
            foreach (string key in candidateKeys)
            {
                if (dictionary.TryGetValue(key, out object directValue) && directValue != null)
                {
                    return directValue;
                }
            }

            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                object nestedValue = FindValueRecursive(pair.Value, candidateKeys);
                if (nestedValue != null)
                {
                    return nestedValue;
                }
            }
        }
        else if (node is List<object> list)
        {
            foreach (object item in list)
            {
                object nestedValue = FindValueRecursive(item, candidateKeys);
                if (nestedValue != null)
                {
                    return nestedValue;
                }
            }
        }

        return null;
    }

    private void SaveSession(string newToken, string newUsername, string newUserId)
    {
        token = newToken;
        username = newUsername;
        userId = newUserId;

        PlayerPrefs.SetString(TokenKey, token);
        PlayerPrefs.SetString(UsernameKey, username);
        PlayerPrefs.SetString(UserIdKey, userId);
        PlayerPrefs.Save();
    }

    private void LoadSession()
    {
        token = PlayerPrefs.GetString(TokenKey, string.Empty);
        username = PlayerPrefs.GetString(UsernameKey, string.Empty);
        userId = PlayerPrefs.GetString(UserIdKey, string.Empty);
    }

    private void ClearSession()
    {
        token = string.Empty;
        username = string.Empty;
        userId = string.Empty;

        PlayerPrefs.DeleteKey(TokenKey);
        PlayerPrefs.DeleteKey(UsernameKey);
        PlayerPrefs.DeleteKey(UserIdKey);
        PlayerPrefs.Save();
    }

    private bool IsTokenExpired(string currentToken)
    {
        if (string.IsNullOrWhiteSpace(currentToken))
        {
            return true;
        }

        string[] parts = currentToken.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            while (payload.Length % 4 != 0)
            {
                payload += "=";
            }

            string decodedPayload = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            object parsed = MiniJson.Json.Deserialize(decodedPayload);
            object expValue = FindValueRecursive(parsed, new[] { "exp" });

            if (expValue is double expDouble)
            {
                DateTimeOffset expiration = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(expDouble));
                return DateTimeOffset.UtcNow >= expiration;
            }

            if (expValue is long expLong)
            {
                DateTimeOffset expiration = DateTimeOffset.FromUnixTimeSeconds(expLong);
                return DateTimeOffset.UtcNow >= expiration;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void LoadAuthenticationScene()
    {
        if (isLoadingScene || SceneManager.GetActiveScene().name == authenticationSceneName)
        {
            return;
        }

        isLoadingScene = true;
        SceneManager.LoadScene(authenticationSceneName);
    }

    private void LoadMenuScene()
    {
        if (isLoadingScene || !IsAuthenticated() || SceneManager.GetActiveScene().name == menuSceneName)
        {
            return;
        }

        isLoadingScene = true;
        SceneManager.LoadScene(menuSceneName);
    }

    private void CacheAuthSceneReferences()
    {
        if (SceneManager.GetActiveScene().name != authenticationSceneName)
        {
            return;
        }

        UIManager uiManager = FindAnyObjectByType<UIManager>();
        if (uiManager != null)
        {
            if (loginPanel == null) loginPanel = uiManager.loginPanel;
            if (registerPanel == null) registerPanel = uiManager.registerPanel;
        }

        if (loginPanel == null) loginPanel = GameObject.Find("LoginPanel");
        if (registerPanel == null) registerPanel = GameObject.Find("RegisterPanel");

        if (loginUsernameInput == null) loginUsernameInput = FindInput(loginPanel, "UsernameInput");
        if (loginPasswordInput == null) loginPasswordInput = FindInput(loginPanel, "PasswordInput");
        if (registerUsernameInput == null) registerUsernameInput = FindInput(registerPanel, "UsernameInput");
        if (registerPasswordInput == null) registerPasswordInput = FindInput(registerPanel, "PasswordInput");

        if (statusLabel == null)
        {
            statusLabel = FindStatusLabel();
        }
    }

    private TMP_InputField FindInput(GameObject panel, string objectName)
    {
        if (panel == null)
        {
            return null;
        }

        Transform[] children = panel.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child.name == objectName)
            {
                return child.GetComponent<TMP_InputField>();
            }
        }

        return null;
    }

    private TMP_Text FindStatusLabel()
    {
        TMP_Text existing = GameObject.Find("AuthStatusLabel")?.GetComponent<TMP_Text>();
        if (existing != null)
        {
            return existing;
        }

        GameObject parent = loginPanel != null ? loginPanel : GameObject.Find("Canvas");
        if (parent == null)
        {
            return null;
        }

        GameObject statusObject = new GameObject("AuthStatusLabel", typeof(RectTransform));
        statusObject.layer = 5;
        statusObject.transform.SetParent(parent.transform, false);

        RectTransform rectTransform = statusObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.anchoredPosition = new Vector2(0f, -30f);
        rectTransform.sizeDelta = new Vector2(480f, 70f);

        TextMeshProUGUI text = statusObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 20;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.color = new Color32(235, 240, 255, 255);
        text.text = string.Empty;

        return text;
    }

    private void EnsureProjectTitle()
    {
        if (SceneManager.GetActiveScene().name != authenticationSceneName)
        {
            return;
        }

        if (projectTitleLabel == null)
        {
            projectTitleLabel = GameObject.Find("ProjectTitleLabel")?.GetComponent<TMP_Text>();
        }

        if (projectTitleLabel == null)
        {
            GameObject canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                return;
            }

            GameObject titleObject = new GameObject("ProjectTitleLabel", typeof(RectTransform));
            titleObject.layer = 5;
            titleObject.transform.SetParent(canvas.transform, false);

            RectTransform rectTransform = titleObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, -12f);
            rectTransform.sizeDelta = new Vector2(760f, 52f);

            TextMeshProUGUI titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 28;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            projectTitleLabel = titleText;
        }

        projectTitleLabel.text = projectDisplayName;
    }

    private void SetStatus(string message, bool isError)
    {
        CacheAuthSceneReferences();

        if (statusLabel != null)
        {
            statusLabel.text = message;
            statusLabel.color = isError ? new Color32(255, 190, 190, 255) : new Color32(230, 242, 255, 255);
        }

        Debug.Log((isError ? "[Auth][Error] " : "[Auth] ") + message);
    }

    private sealed class RequestResponse
    {
        public string Url;
        public long StatusCode;
        public bool Success;
        public string Body;
        public string Error;
        public Dictionary<string, string> Headers;
    }
}
