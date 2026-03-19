using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    [Header("Optional Scene References")]
    [SerializeField] private TMP_Text welcomeText;
    [SerializeField] private TMP_Text projectTitleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_InputField scoreInput;
    [SerializeField] private TMP_Text leaderboardText;

    private static readonly string[] LeaderboardPaths =
    {
        "/api/usuarios",
        "/api/scores",
        "/api/score",
        "/scores",
        "/score",
        "/api/users/scores",
        "/users/scores",
        "/api/auth/scores"
    };

    private static readonly string[] ScoreUpdatePaths =
    {
        "/api/usuarios"
    };

    private static readonly string[] ScoreMethods =
    {
        "PATCH"
    };

    private bool isBusy;

    private void Start()
    {
        if (AuthManager.Instance == null || !AuthManager.Instance.IsAuthenticated())
        {
            SceneManager.LoadScene(AuthManager.Instance != null ? AuthManager.Instance.AuthenticationSceneName : "SampleScene");
            return;
        }

        CacheSceneReferences();

        // Destruir LeaderboardText existente si lo hay (para evitar duplicados)
        if (leaderboardText != null)
        {
            Destroy(leaderboardText.gameObject);
            leaderboardText = null;
        }

        // Destruir scroll view previo si existe
        GameObject oldScroll = GameObject.Find("LeaderboardScrollView");
        if (oldScroll != null) Destroy(oldScroll);

        // Crear siempre el scroll de puntajes en posicion fija
        CreateDynamicLeaderboardScroll();

        RefreshStaticTexts();
        StartCoroutine(RefreshLeaderboard());
    }

    public void Logout()
    {
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.Logout();
        }
    }

    public void SubmitScore()
    {
        if (isBusy)
        {
            return;
        }

        CacheSceneReferences();

        if (scoreInput == null)
        {
            SetStatus("No existe el campo para capturar el score.", true);
            return;
        }

        if (!int.TryParse(scoreInput.text.Trim(), out int score))
        {
            SetStatus("El score debe ser un numero entero.", true);
            return;
        }

        StartCoroutine(SubmitScoreRoutine(score));
    }

    public void RefreshLeaderboardButton()
    {
        if (!isBusy)
        {
            StartCoroutine(RefreshLeaderboard());
        }
    }

    private IEnumerator SubmitScoreRoutine(int score)
    {
        if (AuthManager.Instance == null || !AuthManager.Instance.IsAuthenticated())
        {
            Logout();
            yield break;
        }

        isBusy = true;
        SetStatus("Actualizando score...", false);

        string[] payloads =
        {
            MiniJson.Json.Serialize(new Dictionary<string, object>
            {
                { "username", AuthManager.Instance.username },
                { "data", new Dictionary<string, object> { { "score", score } } }
            }),
            MiniJson.Json.Serialize(new Dictionary<string, object>
            {
                { "username", AuthManager.Instance.username },
                { "data", new Dictionary<string, object> { { "puntaje", score } } }
            })
        };

        RequestResponse lastResponse = null;
        bool submitted = false;

        foreach (string path in ScoreUpdatePaths)
        {
            foreach (string method in ScoreMethods)
            {
                foreach (string payload in payloads)
                {
                    yield return SendAuthorizedJsonRequest(AuthManager.Instance.ApiBaseUrl + path, method, payload, response => lastResponse = response);

                    if (lastResponse == null)
                    {
                        continue;
                    }

                    if (lastResponse.StatusCode == 404)
                    {
                        continue;
                    }

                    if (lastResponse.Success)
                    {
                        submitted = true;
                        break;
                    }

                    if (lastResponse.StatusCode != 400 && lastResponse.StatusCode != 422)
                    {
                        break;
                    }
                }

                if (submitted || (lastResponse != null && lastResponse.StatusCode != 404 && lastResponse.StatusCode != 400 && lastResponse.StatusCode != 422))
                {
                    break;
                }
            }

            if (submitted || (lastResponse != null && lastResponse.StatusCode != 404 && lastResponse.StatusCode != 400 && lastResponse.StatusCode != 422))
            {
                break;
            }
        }

        isBusy = false;

        if (!submitted)
        {
            SetStatus(BuildErrorMessage(lastResponse, "No fue posible actualizar el score."), true);
            yield break;
        }

        SetStatus("Score actualizado correctamente.", false);
        scoreInput.text = string.Empty;
        StartCoroutine(RefreshLeaderboard());
    }

    private IEnumerator RefreshLeaderboard()
    {
        if (AuthManager.Instance == null || !AuthManager.Instance.IsAuthenticated())
        {
            Logout();
            yield break;
        }

        isBusy = true;
        SetStatus("Consultando tabla de puntajes...", false);

        RequestResponse lastResponse = null;

        foreach (string path in LeaderboardPaths)
        {
            yield return SendAuthorizedJsonRequest(AuthManager.Instance.ApiBaseUrl + path, UnityWebRequest.kHttpVerbGET, null, response => lastResponse = response);

            if (lastResponse == null)
            {
                continue;
            }

            if (lastResponse.StatusCode == 404)
            {
                continue;
            }

            break;
        }

        isBusy = false;

        if (lastResponse == null || !lastResponse.Success)
        {
            SetStatus(BuildErrorMessage(lastResponse, "No fue posible consultar la tabla de puntajes."), true);
            if (leaderboardText != null)
            {
                leaderboardText.text = "No se pudo cargar el ranking.";
            }
            yield break;
        }

        List<ScoreEntry> entries = ParseLeaderboard(lastResponse.Body);
        entries.Sort((left, right) => right.Score.CompareTo(left.Score));

        if (leaderboardText != null)
        {
            leaderboardText.text = entries.Count == 0 ? "No hay puntajes disponibles." : BuildLeaderboardText(entries);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(leaderboardText.rectTransform);
        }

        SetStatus("Tabla de puntajes actualizada.", false);
    }

    private IEnumerator SendAuthorizedJsonRequest(string url, string method, string jsonPayload, Action<RequestResponse> onComplete)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            AuthManager.Instance.ApplyAuthorizationHeader(request);

            if (method != UnityWebRequest.kHttpVerbGET)
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload ?? string.Empty);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            yield return request.SendWebRequest();

            RequestResponse response = new RequestResponse
            {
                StatusCode = request.responseCode,
                Success = request.result == UnityWebRequest.Result.Success,
                Body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                Error = request.error
            };

            onComplete?.Invoke(response);
        }
    }

    private void CacheSceneReferences()
    {
        welcomeText = welcomeText != null ? welcomeText : GameObject.Find("WelcomeText")?.GetComponent<TMP_Text>();
        projectTitleText = projectTitleText != null ? projectTitleText : GameObject.Find("MenuProjectTitle")?.GetComponent<TMP_Text>();
        statusText = statusText != null ? statusText : GameObject.Find("MenuStatusText")?.GetComponent<TMP_Text>();
        scoreInput = scoreInput != null ? scoreInput : GameObject.Find("ScoreInput")?.GetComponent<TMP_InputField>();
        leaderboardText = leaderboardText != null ? leaderboardText : GameObject.Find("LeaderboardText")?.GetComponent<TMP_Text>();
    }

    private void WrapInScrollRect(TMP_Text textComponent)
    {
        RectTransform originalRect = textComponent.rectTransform;
        Transform originalParent = originalRect.parent;

        Vector2 origAnchorMin = originalRect.anchorMin;
        Vector2 origAnchorMax = originalRect.anchorMax;
        Vector2 origPivot = originalRect.pivot;
        Vector2 origAnchoredPosition = originalRect.anchoredPosition;
        Vector2 origSizeDelta = originalRect.sizeDelta;

        GameObject scrollViewObj = new GameObject("LeaderboardScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollViewObj.layer = textComponent.gameObject.layer;
        scrollViewObj.transform.SetParent(originalParent, false);

        RectTransform scrollRectTransform = scrollViewObj.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = origAnchorMin;
        scrollRectTransform.anchorMax = origAnchorMax;
        scrollRectTransform.pivot = origPivot;
        scrollRectTransform.anchoredPosition = origAnchoredPosition;
        scrollRectTransform.sizeDelta = origSizeDelta;

        // Añadir fondo transparente solo si se desea, lo dejaremos transparente completo
        Image bgImage = scrollViewObj.GetComponent<Image>();
        bgImage.color = new Color32(0, 0, 0, 0);

        GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObj.layer = textComponent.gameObject.layer;
        viewportObj.transform.SetParent(scrollViewObj.transform, false);

        RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportRect.anchoredPosition = Vector2.zero;

        originalRect.SetParent(viewportObj.transform, false);
        originalRect.anchorMin = new Vector2(0f, 1f); // Stretch horizontally
        originalRect.anchorMax = new Vector2(1f, 1f);
        originalRect.pivot = new Vector2(0.5f, 1f);   // Top pivot
        originalRect.sizeDelta = new Vector2(0f, 100f); // Width offset 0, initial height
        originalRect.anchoredPosition = new Vector2(0f, 0f);

        UnityEngine.UI.ContentSizeFitter fitter = textComponent.gameObject.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (fitter == null) fitter = textComponent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect sr = scrollViewObj.GetComponent<ScrollRect>();
        sr.content = originalRect;
        sr.viewport = viewportRect;
        sr.horizontal = false;
        sr.vertical = true;
        sr.scrollSensitivity = 30f;
    }

    private void CreateDynamicLeaderboardScroll()
    {
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null) return;
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();

        GameObject scrollViewObj = new GameObject("LeaderboardScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollViewObj.layer = 5;
        scrollViewObj.transform.SetParent(canvasRect, false);

        RectTransform scrollRectTransform = scrollViewObj.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0.5f, 1f);
        scrollRectTransform.anchorMax = new Vector2(0.5f, 1f);
        scrollRectTransform.pivot = new Vector2(0.5f, 1f);
        scrollRectTransform.anchoredPosition = new Vector2(0f, -340f);
        scrollRectTransform.sizeDelta = new Vector2(540f, 300f);

        Image bgImage = scrollViewObj.GetComponent<Image>();
        bgImage.color = new Color32(0, 0, 0, 0);

        GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObj.layer = 5;
        viewportObj.transform.SetParent(scrollViewObj.transform, false);

        RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportRect.anchoredPosition = Vector2.zero;

        GameObject contentObj = new GameObject("LeaderboardText", typeof(RectTransform));
        contentObj.layer = 5;
        contentObj.transform.SetParent(viewportObj.transform, false);

        RectTransform contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = new Vector2(0f, 100f);
        contentRect.anchoredPosition = new Vector2(0f, 0f);

        leaderboardText = contentObj.AddComponent<TextMeshProUGUI>();
        leaderboardText.fontSize = 22;
        leaderboardText.fontStyle = FontStyles.Normal;
        leaderboardText.color = new Color32(232, 242, 255, 255);
        leaderboardText.alignment = TextAlignmentOptions.Top;
        leaderboardText.textWrappingMode = TextWrappingModes.Normal;

        UnityEngine.UI.ContentSizeFitter fitter = contentObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect sr = scrollViewObj.GetComponent<ScrollRect>();
        sr.content = contentRect;
        sr.viewport = viewportRect;
        sr.horizontal = false;
        sr.vertical = true;
        sr.scrollSensitivity = 30f;
    }

    private void BuildRuntimeMenuUi()
    {
        GameObject canvas = GameObject.Find("Canvas");
        if (canvas == null)
        {
            return;
        }

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();

        if (projectTitleText == null)
        {
            projectTitleText = CreateText("MenuProjectTitle", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(760f, 44f), 28, FontStyles.Bold);
        }

        if (statusText == null)
        {
            statusText = CreateText("MenuStatusText", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(760f, 44f), 20, FontStyles.Normal);
        }

        if (scoreInput == null)
        {
            scoreInput = CreateInputField("ScoreInput", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-100f, -118f), new Vector2(220f, 42f), "Nuevo score");
        }

        if (GameObject.Find("SaveScoreButton") == null)
        {
            CreateButton("SaveScoreButton", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(120f, -118f), new Vector2(180f, 42f), "Actualizar Score", SubmitScore);
        }

        if (GameObject.Find("RefreshLeaderboardButton") == null)
        {
            CreateButton("RefreshLeaderboardButton", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -172f), new Vector2(220f, 38f), "Refrescar Tabla", RefreshLeaderboardButton);
        }

        if (GameObject.Find("LeaderboardTitle") == null)
        {
            TMP_Text leaderboardTitle = CreateText("LeaderboardTitle", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -218f), new Vector2(460f, 36f), 24, FontStyles.Bold);
            leaderboardTitle.text = "Tabla de Puntajes";
        }

        if (leaderboardText == null)
        {
            leaderboardText = CreateText("LeaderboardText", canvasRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(540f, 300f), 22, FontStyles.Normal);
            leaderboardText.alignment = TextAlignmentOptions.TopLeft;
        }
    }

    private void RefreshStaticTexts()
    {
        if (projectTitleText != null)
        {
            projectTitleText.text = AuthManager.Instance.ProjectDisplayName;
            projectTitleText.color = Color.white;
            projectTitleText.alignment = TextAlignmentOptions.Center;
        }

        if (welcomeText != null)
        {
            welcomeText.text = "Bienvenido, " + AuthManager.Instance.username;
        }

        if (leaderboardText != null && string.IsNullOrWhiteSpace(leaderboardText.text))
        {
            leaderboardText.text = "Cargando puntajes...";
        }
    }

    private string BuildLeaderboardText(List<ScoreEntry> entries)
    {
        List<string> lines = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            lines.Add((i + 1) + ". " + entries[i].Username + " - " + entries[i].Score);
        }

        return string.Join("\n", lines);
    }

    private List<ScoreEntry> ParseLeaderboard(string json)
    {
        List<ScoreEntry> entries = new List<ScoreEntry>();

        object parsed = null;
        try
        {
            parsed = MiniJson.Json.Deserialize(json);
        }
        catch
        {
            return entries;
        }

        List<object> rows = FindFirstList(parsed);
        if (rows == null)
        {
            return entries;
        }

        foreach (object row in rows)
        {
            Dictionary<string, object> item = row as Dictionary<string, object>;
            if (item == null)
            {
                continue;
            }

            string user = GetString(item, "username", "userName", "email", "correo", "name", "nombre");
            int score = GetInt(item, "score", "puntaje", "points", "value");
            if (score == 0 && item.TryGetValue("data", out object dataValue))
            {
                Dictionary<string, object> data = dataValue as Dictionary<string, object>;
                if (data != null)
                {
                    score = GetInt(data, "score", "puntaje", "points", "value");
                }
            }

            if (string.IsNullOrWhiteSpace(user))
            {
                user = "Usuario";
            }

            entries.Add(new ScoreEntry { Username = user, Score = score });
        }

        return entries;
    }

    private List<object> FindFirstList(object node)
    {
        if (node is List<object> list)
        {
            return list;
        }

        if (node is Dictionary<string, object> dictionary)
        {
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                List<object> nested = FindFirstList(pair.Value);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private string GetString(Dictionary<string, object> item, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (item.TryGetValue(key, out object value) && value != null)
            {
                return value.ToString();
            }
        }

        return string.Empty;
    }

    private int GetInt(Dictionary<string, object> item, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (item.TryGetValue(key, out object value) && value != null)
            {
                if (value is double doubleValue)
                {
                    return Convert.ToInt32(doubleValue);
                }

                if (value is long longValue)
                {
                    return Convert.ToInt32(longValue);
                }

                if (int.TryParse(value.ToString(), out int parsed))
                {
                    return parsed;
                }
            }
        }

        return 0;
    }

    private string BuildErrorMessage(RequestResponse response, string fallback)
    {
        if (response == null)
        {
            return fallback;
        }

        string parsedMessage = string.Empty;
        try
        {
            object parsed = MiniJson.Json.Deserialize(response.Body);
            if (parsed is Dictionary<string, object> dictionary)
            {
                parsedMessage = GetString(dictionary, "msg", "message", "error", "detail");
            }
        }
        catch
        {
            parsedMessage = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(parsedMessage))
        {
            return parsedMessage;
        }

        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            return response.Error;
        }

        return fallback;
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = isError ? new Color32(255, 200, 200, 255) : new Color32(232, 242, 255, 255);
        }

        Debug.Log((isError ? "[Menu][Error] " : "[Menu] ") + message);
    }

    private TMP_Text CreateText(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, int fontSize, FontStyles style)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.layer = 5;
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = new Color32(232, 242, 255, 255);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;

        return text;
    }

    private Button CreateButton(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, string caption, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.layer = 5;
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color32(236, 236, 236, 255);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        TMP_Text buttonLabel = CreateText(objectName + "_Label", rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 20, FontStyles.Bold);
        buttonLabel.text = caption;
        buttonLabel.color = new Color32(36, 48, 68, 255);
        buttonLabel.alignment = TextAlignmentOptions.Center;

        return button;
    }

    private TMP_InputField CreateInputField(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, string placeholder)
    {
        GameObject inputObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputObject.layer = 5;
        inputObject.transform.SetParent(parent, false);

        RectTransform rectTransform = inputObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image background = inputObject.GetComponent<Image>();
        background.color = Color.white;

        TMP_InputField inputField = inputObject.GetComponent<TMP_InputField>();
        inputField.contentType = TMP_InputField.ContentType.IntegerNumber;

        GameObject textAreaObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textAreaObject.layer = 5;
        textAreaObject.transform.SetParent(inputObject.transform, false);

        RectTransform textAreaRect = textAreaObject.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10f, 6f);
        textAreaRect.offsetMax = new Vector2(-10f, -7f);

        TMP_Text placeholderText = CreateText("Placeholder", textAreaRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 20, FontStyles.Italic);
        placeholderText.text = placeholder;
        placeholderText.color = new Color32(120, 120, 120, 255);
        placeholderText.alignment = TextAlignmentOptions.Left;

        TMP_Text valueText = CreateText("Text", textAreaRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 20, FontStyles.Normal);
        valueText.text = string.Empty;
        valueText.color = new Color32(32, 32, 32, 255);
        valueText.alignment = TextAlignmentOptions.Left;

        inputField.textViewport = textAreaRect;
        inputField.textComponent = (TextMeshProUGUI)valueText;
        inputField.placeholder = placeholderText;

        return inputField;
    }

    private sealed class RequestResponse
    {
        public long StatusCode;
        public bool Success;
        public string Body;
        public string Error;
    }

    private sealed class ScoreEntry
    {
        public string Username;
        public int Score;
    }
}
