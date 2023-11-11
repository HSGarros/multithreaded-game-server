using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

using System.Threading;

class Program
{
    static string[] usernameList = new string[] { "peach", "Lemon", "Pear", "Banana", "grape", "plum", "honeydew", "orange", "tangerine", "papaya", "coconut", "Mango", "pineapple", "Cherry", "Pitaya", "Durin" };
    static int num = 0;
    static Stack<string> usernameStack = new Stack<string>(usernameList); 

    static Mutex mutex = new Mutex();
    static Dictionary<string, Thread> threadDict = new Dictionary<string, Thread>();  //(player -> Thread)
    static Dictionary<string, string> match = new Dictionary<string, string>();   //(player -> id)
    static Dictionary<string, Thread[]> re_match = new Dictionary<string, Thread[]>();   //(id -> Thread*2)
    static Dictionary<string, string[]> idToPlayer = new Dictionary<string, string[]>();   //( id -> player*2)
    static Dictionary<string, object> Matchlock = new Dictionary<string, object>(); //(id -> lock)
    static string wait_thread = null;

    static Dictionary<string, string> move = new Dictionary<string, string>(); //(id -> step)
    static Dictionary<string, bool> flagList = new Dictionary<string, bool>(); //(id -> flag)

    static void HandleClient(Socket clientSocket) 
    {
        Socket client = clientSocket;
        Console.WriteLine("Connection established with "+ client.RemoteEndPoint.ToString());
        num++;
        Thread.CurrentThread.Name = num.ToString();
        try
        {
            while (true)
            {
                byte[] buffer = new byte[1024];
                int bytesReceived = client.Receive(buffer);
                string request = Encoding.ASCII.GetString(buffer, 0, bytesReceived);
                string[] requestLines = request.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                string requestPath = "";
                foreach (string line in requestLines)
                {
                    if (line.StartsWith("GET "))
                    {
                        requestPath = line.Substring(4);
                        int index = requestPath.IndexOf(" ");
                        if (index != -1)
                        {
                            requestPath = requestPath.Substring(0, index);
                        }
                    }
                }

                string response = "";
                string endpoints = "";
                string user_info = "";
                string httpResponse = "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n";
                byte[] httpResponseBytes = null;
                if (requestPath == "/register")
                {
                    Console.WriteLine("Thread " + Thread.CurrentThread.Name+ " sent response to " + client.RemoteEndPoint.ToString()+ " for /register");
                    if (usernameStack.Count != 0)
                    {
                        response = "<html><body><h1>" + usernameStack.Pop() + "</h1></body></html>";
                        httpResponse = httpResponse + response;
                        httpResponseBytes = Encoding.ASCII.GetBytes(httpResponse);
                    }
                    else
                    {
                        response = "<html><body><h1>No idle users</h1></body></html>";
                        httpResponse = httpResponse + response;
                        httpResponseBytes = Encoding.ASCII.GetBytes(httpResponse);
                    }
                    client.Send(httpResponseBytes);

                    /*client.Shutdown(SocketShutdown.Both);
                    client.Close();
                    break;*/
                }
                else if (requestPath == "/favicon.ico")
                {
                    Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + client.RemoteEndPoint.ToString() + " for /favicon.ico");

                    /*httpResponse = "HTTP/1.1 200 OK\r\nContent-Type: image/x-icon\r\n\r\n";
                    httpResponseBytes = Encoding.ASCII.GetBytes(httpResponse).Concat(File.ReadAllBytes("bitbug_favicon.ico")).ToArray();*/
                    response = "<html><body><h1>favicon</h1></body></html>";
                    httpResponse = httpResponse + response;
                    httpResponseBytes = Encoding.ASCII.GetBytes(httpResponse);

                    client.Send(httpResponseBytes);
                    /*client.Shutdown(SocketShutdown.Both);
                    client.Close();
                    break;*/
                }
                else
                {
                    int index = requestPath.IndexOf("?");
                    if (index != -1)
                    {
                        endpoints = requestPath.Substring(0, index);
                        user_info = requestPath.Substring(index + 1);
                    }
                    string player = GetValueFromQueryString(user_info, "player");



                    if (usernameList.Contains(player))
                    {
                        if (endpoints == "/pairme")
                        {
                            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + client.RemoteEndPoint.ToString() + " for " + requestPath);
                            mutex.WaitOne();

                            player = GetValueFromQueryString(user_info, "player");
                            if (!threadDict.ContainsKey(player) && wait_thread == null)
                            {
                                wait_thread = player;
                                threadDict[player] = null;
                                response = "matching";
                            }
                            else if (!threadDict.ContainsKey(player) && wait_thread != null)
                            {

                                string id = GenerateRandomString();//随机匹配id
                                move[id] = "-1";
                                Matchlock[id] = new object();
                                bool flag = true;
                                flagList[id] = flag;
                                Thread thread1 = new Thread(() => black(Matchlock[id], id, flag));
                                Thread thread2 = new Thread(() => white(Matchlock[id], id, flag));

                                threadDict[wait_thread] = thread1;

                                threadDict[player] = thread2;
                                match[wait_thread] = id;
                                match[player] = id;
                                re_match[id] = new Thread[] { thread1, thread2 };
                                idToPlayer[id] = new string[] { player, wait_thread };
                                wait_thread = null;

                                response = id + "Match success you back";
                                Console.WriteLine(id);

                               thread1.Start();
                                thread2.Start();


                            }
                            else if (threadDict.ContainsKey(player) && wait_thread != player)
                            {

                                response = match[player] + "Match finish";


                            }
                            else if (threadDict.ContainsKey(player) && wait_thread == player)
                            {
                                response = "stilling matching";
                            }
                            mutex.ReleaseMutex();

                        }
                        else if (endpoints == "/mymove")
                        {
                            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + client.RemoteEndPoint.ToString() + " for " + requestPath);
                            // 提取 id 的值
                            string id = GetValueFromQueryString(user_info, "id");

                            // 提取 move 的值
                            string step = GetValueFromQueryString(user_info, "move");

                            if (match[player]==id && flagList[id])
                            {
                                Thread thread1 = threadDict[player];
                                if (thread1.ThreadState == ThreadState.WaitSleepJoin)
                                {
                                    response = "refuse";
                                }
                                else
                                {
                                    if (move[id] == "-1")
                                    {
                                        move[id] = step;
                                        response = "accept";
                                    }
                                    else
                                    {
                                        response = "refuse";
                                    }

                                }
                            }
                            else
                            {
                                response = "wrong request";
                            }


                            


                        }
                        else if (endpoints == "/theirmove")
                        {
                            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + client.RemoteEndPoint.ToString() + " for " + requestPath);
                            string id = GetValueFromQueryString(user_info, "id");

                            if (match[player] == id && flagList[id])
                            {
                                Thread thread1 = threadDict[player];
                                if (thread1.ThreadState != ThreadState.WaitSleepJoin)
                                {
                                    response = "-1";
                                }
                                else
                                {
                                    response = move[id];
                                    move[id] = "-1";
                                }
                            }
                            else
                            {
                                response = "wrong request";
                            }




                        }
                        else if (endpoints == "/quit")
                        {
                            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " sent response to " + client.RemoteEndPoint.ToString() + " for " + requestPath);
                            string id = GetValueFromQueryString(user_info, "id");

                            if (match[player] == id && flagList[id])
                            {
                                Thread thread1 = re_match[id][0];
                                Thread thread2 = re_match[id][1];


                                flagList[id] = false;

                                string[] s = idToPlayer[id];
                                threadDict.Remove(s[0]);
                                threadDict.Remove(s[1]);
                                response = "quit success";

                            }
                            else
                            {
                                response = "wrong request";
                            }
                            
                        }
                        else
                        {
                            response = "Unknown request";
                        }

                    }
                    else
                    {
                        response = "Illegal user";
                    }


                    httpResponse = "HTTP/1.1 200 OK\r\n" +
                              "Content-Type: text/plain\r\n" +
                              "Access-Control-Allow-Origin: *\r\n" + // 设置允许访问的源，此处使用通配符允许所有源
                              "Content-Length: " + response.Length + "\r\n" +
                              "\r\n"
                              + response;

                    httpResponseBytes = Encoding.ASCII.GetBytes(httpResponse);

                    client.Send(httpResponseBytes);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Thread " + Thread.CurrentThread.Name + " closing connection with  " + client.RemoteEndPoint.ToString() + " and terminating");
        }
    }

    static void black(object Matchlock, string id, bool flag)
    {
        while (flag)
        {
            lock (Matchlock)
            {
                // 等待轮到自己执行
                Monitor.Wait(Matchlock);

                // 执行工作

                while (move[id] == "-1")
                {
                }

                // 接收到了移动

                while (move[id] != "-1")
                {
                }

                // 另一个线程接收到了移动



                // 通知另一个线程执行
                Monitor.Pulse(Matchlock);
            }
        }
    }

    static void white(object Matchlock, string id , bool flag)
    {
        while (flag)
        {
            lock (Matchlock)
            {
                // 通知另一个线程执行
                Monitor.Pulse(Matchlock);

                // 等待轮到自己执行
                Monitor.Wait(Matchlock);

                // 执行工作

                while (move[id] == "-1")
                {
                }

                // 接收到了移动

                while (move[id] != "-1")
                {
                }

                // 另一个线程接收到了移动

            }
        }
        
    }
    static string GetValueFromQueryString(string queryString, string parameter)
    {
        string pattern = $"{parameter}=(.*?)(&|$)";
        Match match = Regex.Match(queryString, pattern);

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
    static string GenerateRandomString()
    {
        int length = 16;
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";

        Random random = new Random();
        string randomString = new string(Enumerable.Repeat(chars, length)
          .Select(s => s[random.Next(s.Length)]).ToArray());

        return randomString;
    }

    static void Main(string[] args)
    {
        

        int port = 8080;
        
        Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPEndPoint IPP =  new IPEndPoint(IPAddress.Any, port);
        listener.Bind(IPP);
        listener.Listen(10);
        Console.WriteLine("Listening at 127.0.0.1:"+ port);

        while (true)
        {
            // 接受客户端连接请求
            Socket clientSocket = listener.Accept();

            // 创建一个新线程来处理连接的客户端
            Thread clientThread = new Thread(() => HandleClient(clientSocket));

            clientThread.Start();
        }

        
    }

    
       
   
}