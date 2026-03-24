using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;
using System.Collections.Generic;
using TMPro;

public class NetworkManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField ipInputField;
    public TMP_InputField portInputField;
    public TMP_InputField messageInputField;
    public TMP_Text chatText;
    public Button serverButton;
    public Button clientButton;
    public Button sendButton;

    private TcpListener server;
    private TcpClient client;
    private NetworkStream stream;
    private Thread networkThread;
    private bool isRunning = false;
    private bool isConnected = false;
    private string connectionType = ""; // "server" или "client"
    private Queue<string> messageQueue = new Queue<string>();
    private object queueLock = new object();

    void Start()
    {
        // Настройка UI
        sendButton.interactable = false;
        
        serverButton.onClick.AddListener(StartAsServer);
        clientButton.onClick.AddListener(StartAsClient);
        sendButton.onClick.AddListener(SendMessage);
        try
        {
            string localIP = GetLocalIPAddress();
            ipInputField.text = localIP;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Не удалось получить IP: {e.Message}");
        }
        
        // Загрузка последнего использованного порта
        if (PlayerPrefs.HasKey("LastPort"))
            portInputField.text = PlayerPrefs.GetString("LastPort");
        else
            portInputField.text = "25001";
    }

    void Update()
    {
        // CheckServerStatus();
        // Обработка сообщений из очереди (выполняется в главном потоке)
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                string msg = messageQueue.Dequeue();
                AddToChat(msg);
            }
        }
    }

    // ============= ЗАПУСК СЕРВЕРА =============
    void StartAsServer()
    {
        
        Debug.Log($"=== ЗАПУСК СЕРВЕРА ===");
        Debug.Log($"Порт: {portInputField.text}");
        Debug.Log($"IP: {GetLocalIPAddress()}");
        if (isRunning)
        {
            AddToChat("Сначала остановите текущее соединение!");
            return;
        }

        int port = int.Parse(portInputField.text);
        PlayerPrefs.SetString("LastPort", portInputField.text);
        
        connectionType = "server";
        networkThread = new Thread(() => RunServer(port));
        networkThread.Start();
        
        serverButton.interactable = false;
        clientButton.interactable = false;
        sendButton.interactable = true;
        
        AddToChat($"Сервер запущен на порту {port}. Ожидание подключения...");
    }

    void RunServer(int port)
    {
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;
        
            AddToChatThreadSafe($"✅ Сервер запущен на порту {port}");
            AddToChatThreadSafe($"🌐 IP адрес сервера: {GetLocalIPAddress()}");
        
            // Цикл для принятия нескольких подключений (если нужно)
            while (isRunning)
            {
                AddToChatThreadSafe("⏳ Ожидание подключения...");
            
                // Принимаем клиента (блокирующая операция)
                client = server.AcceptTcpClient();
                isConnected = true;
            
                AddToChatThreadSafe("✅ Клиент подключился! Можно обмениваться сообщениями.");
            
                stream = client.GetStream();
                byte[] buffer = new byte[4096];
                int bytesRead;
            
                // Цикл приема сообщений от текущего клиента
                while (isRunning && isConnected && (bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    AddToChatThreadSafe($"📩 Получено: {receivedMessage}");
                }
            
                // Клиент отключился
                if (isConnected)
                {
                    AddToChatThreadSafe("🔌 Клиент отключился");
                    isConnected = false;
                    stream?.Close();
                    client?.Close();
                }
            }
        }
        catch (System.Exception e)
        {
            AddToChatThreadSafe($"❌ Ошибка сервера: {e.Message}");
        }
        finally
        {
            StopConnection();
        }
    }

    // ============= ПОДКЛЮЧЕНИЕ КАК КЛИЕНТ =============
    void StartAsClient()
    {
        if (isRunning)
        {
            AddToChat("Сначала остановите текущее соединение!");
            return;
        }

        string ip = ipInputField.text;
        int port = int.Parse(portInputField.text);
        PlayerPrefs.SetString("LastPort", portInputField.text);
        
        if (string.IsNullOrEmpty(ip))
        {
            AddToChat("Введите IP адрес сервера!");
            return;
        }
        
        connectionType = "client";
        networkThread = new Thread(() => RunClient(ip, port));
        networkThread.Start();
        
        serverButton.interactable = false;
        clientButton.interactable = false;
        sendButton.interactable = true;
        
        AddToChat($"Подключение к {ip}:{port}...");
    }

    void RunClient(string ip, int port)
    {
        try
        {
            client = new TcpClient();
            client.Connect(IPAddress.Parse(ip), port);
            isConnected = true;
            isRunning = true;
            
            AddToChatThreadSafe("✅ Подключено к серверу! Можно обмениваться сообщениями.");
            
            stream = client.GetStream();
            byte[] buffer = new byte[4096];
            int bytesRead;

            // Цикл приема сообщений
            while (isRunning && (bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                AddToChatThreadSafe($"📩 Получено: {receivedMessage}");
            }
        }
        catch (System.Exception e)
        {
            AddToChatThreadSafe($"❌ Ошибка подключения: {e.Message}");
            StopConnection();
        }
        finally
        {
            if (!isConnected)
                StopConnection();
        }
    }

    // ============= ОТПРАВКА СООБЩЕНИЙ =============
    void SendMessage()
    {
        if (!isConnected)
        {
            AddToChat("Нет активного соединения!");
            return;
        }
        
        string message = messageInputField.text;
        if (string.IsNullOrEmpty(message))
            return;
            
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            stream.Write(data, 0, data.Length);
            AddToChat($"📤 Отправлено: {message}");
            messageInputField.text = "";
        }
        catch (System.Exception e)
        {
            AddToChat($"❌ Ошибка отправки: {e.Message}");
            StopConnection();
        }
    }

    // ============= ОСТАНОВКА СОЕДИНЕНИЯ =============
    void StopConnection()
    {
        isRunning = false;
        isConnected = false;
        
        try
        {
            stream?.Close();
            client?.Close();
            server?.Stop();
        }
        catch { }
        
        // Возвращаем UI в исходное состояние
        UnityMainThreadDispatcher.Instance().Execute(() => {
            serverButton.interactable = true;
            clientButton.interactable = true;
            sendButton.interactable = false;
            AddToChat("🔌 Соединение закрыто");
        });
    }
    
    void CheckServerStatus()
    {
        if (server != null)
        {
            Debug.Log($"Сервер запущен: {server.Server.IsBound}");
            Debug.Log($"Локальная точка: {server.LocalEndpoint}");
        }
        else
        {
            Debug.Log("Сервер НЕ запущен!");
        }
    }

    // ============= ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =============
    void AddToChat(string message)
    {
        chatText.text += $"{System.DateTime.Now:HH:mm:ss} - {message}\n";
        // Автопрокрутка вниз
        Canvas.ForceUpdateCanvases();
        var scrollRect = chatText.GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0;
    }
    
    void AddToChatThreadSafe(string message)
    {
        lock (queueLock)
        {
            messageQueue.Enqueue(message);
        }
    }
    
    void OnApplicationQuit()
    {
        StopConnection();
        networkThread?.Join(1000);
    }
    
    string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        for (int i = 0; i < host.AddressList.Length; i++)
        {
            Debug.Log(host.AddressList[i].ToString());
        }
        foreach (var ip in host.AddressList)
        {
            // AddressFamily.InterNetwork = IPv4 (игнорируем IPv6)
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return host.AddressList[2].ToString();
            }
        }
        throw new System.Exception("Адаптер IPv4 не найден!");
    }
}