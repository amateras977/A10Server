
using System;

using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using System.Linq;

using System.Collections.Generic;
using System.IO.Ports;
using System.Diagnostics;

namespace A10Server
{
    class Program
    {
        static void Main(string[] args) => MainAsync(args).Wait();
        async static Task MainAsync(string[] args)
        {
            Console.WriteLine("Start A10Server");
            InvokeHttpServer();
            Thread.Sleep(5000);
        }

        static void InvokeHttpServer()
        {
            try
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:8080/");

                listener.Start();
                
                A10Piston.Open();
                var task = Task.Run(() =>
                {
                    A10Piston.Start();
                });

                while (true)
                {
                    HttpListenerContext context = listener.GetContext();

                    Dispatcher(context);
                    HttpListenerResponse res = context.Response;

                    res.StatusCode = 200;
                    byte[] content = Encoding.UTF8.GetBytes("Http Request Recieved.");
                    res.OutputStream.Write(content, 0, content.Length);
                    res.Close();
                }

                task.Wait();
            } catch(Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
            A10Piston.Close();
        }

        private static string apiUrlPrefix ="/api";

        private static Dictionary<string, Func<HttpListenerContext, HttpListenerResponse>> urlMap = new Dictionary<string, System.Func<HttpListenerContext,HttpListenerResponse>>()
        {
            { $"{apiUrlPrefix}/addQueue", (context) => 
                { 
                    Console.WriteLine("/addQueue");

                    string intervalStr = context.Request.QueryString.Get("interval");
                    float interval = float.Parse(intervalStr);

                    string directionStr = context.Request.QueryString.Get("direction");
                    int direction = int.Parse(directionStr);
                    direction = direction < 0 ? -1 : 1;

                    A10Piston.AddQueue(interval, direction);
                    return context.Response;
                } },
            { $"{apiUrlPrefix}/clearQueue", (context) => 
                { 
                    Console.WriteLine("/clearQueue");
                    A10Piston.ClearQueue();
                    return context.Response;
                } },


        };
        static void Dispatcher(HttpListenerContext context)
        {
            var request = context.Request;
                
            if (request.RawUrl.Contains(apiUrlPrefix))
            {
                string querySeparator = "?";
                string url = request.RawUrl.Contains(querySeparator) ? request.RawUrl.Split("?", 2)[0] : request.RawUrl;

                Func<HttpListenerContext, HttpListenerResponse> tgtFunc;
                urlMap.TryGetValue(url, out tgtFunc);

                if(tgtFunc != null) { tgtFunc.Invoke(context); }


                Console.WriteLine($"url: {url}");
                if (request.QueryString.AllKeys.Length > 0)
                {
                    Console.WriteLine($" keys: {request.QueryString.AllKeys.Aggregate((all, key) => all + key)}");
                }
                Console.WriteLine($"value(speed): {request.QueryString.Get("speed")}");
                Console.WriteLine($"raw url:{request.RawUrl}");
            }
        }
    }
    public static class A10Piston
    {
        private struct A10PistonCommand
        {
            public float interval;
            public int direction;
        };
        private static SerialPort port;

        private static Queue<A10PistonCommand> commandQueue = new Queue<A10PistonCommand>();

        private static float lastExecuteTime = 0f;
        private static float executingCommandInterval = 0f;
        private static float nextExecuteTime = 0f;

        private static float lastTime = 0f;

        // A10Piston Minimum allowable command interval
        // (If below, the command may be ignored.)
        private static float minimumInterval = 0.2f;

        static void init()
        {
            port = new SerialPort();
            port.BaudRate = 19200;
            port.Parity = Parity.None;
            port.DataBits = 8;
            port.StopBits = StopBits.One;
            port.Handshake = Handshake.None;
            port.PortName = "COM4";
        }
        public static void AddQueue(float interval, int direction)
        {
            Console.WriteLine("AddQueue interval: {interval}, direction: {direction}");
            var command = new A10PistonCommand();
            command.interval = interval;
            command.direction = direction * -1;
            commandQueue.Enqueue(command);

        }
        public static void ClearQueue()
        {
            commandQueue.Clear();

            // reset timers
            executingCommandInterval = 0f;
            lastExecuteTime = 0f;
            nextExecuteTime = 0f;
        }
        public static void Open()
        {
            init();
            port.Open();

            // Send WakeUp signal
            port.Write(new byte[] { 3, 1, 0 }, 0, 3);
            InitPosition();
        }
        public static void Start()
        {
            var sw = new Stopwatch();
            sw.Start();

            float lastTime = 0f;
            while(true)
            {

                float currentTime = (float) sw.ElapsedMilliseconds / 1000;

                Sync(currentTime);

                lastTime = currentTime;
                Thread.Sleep(1);
            }
        }
        public static void InitPosition ()
        {
            Thread.Sleep((int) minimumInterval * 100);

            // Forced transition to the front
            port.Write(new byte[] { 3, 0, 60 }, 0, 3);
        }
        public static void Sync(float currentTime)
        {
            if (currentTime >= nextExecuteTime)
            {
                float diff = lastTime - currentTime;
                if (diff > 0)
                {
                    Console.WriteLine($" currentTime: {currentTime}, lastTime: {lastTime}, diff: {diff}");
                }
                lastTime = currentTime;
                A10PistonCommand command;
                if (commandQueue.Count > 0)
                {
                    command = commandQueue.Dequeue();
                    PublishCommnand(command);

                    executingCommandInterval = command.interval <= minimumInterval ? minimumInterval : command.interval;
                    Console.WriteLine($" currentTime: {currentTime}, nextExecuteTime: {nextExecuteTime}, diff: {nextExecuteTime - currentTime}, interval: {command.interval}");

                    nextExecuteTime = currentTime + executingCommandInterval;
                    lastExecuteTime = currentTime;
                }
            }
        }

        private static void PublishCommnand(A10PistonCommand command)
        {
            // 10-60 (The closer to 0, the slower.)
            //
            // 10 or more recommended. 
            // Is less than 10 not recommended, the burden on the motor is very heavy
            // 
            byte speed = ResolveSpeed(command.interval, command.direction);

            // 0-200 (Front is 0.)
            //
            byte position = (byte)(command.direction > 0 ? 200 : 0);

            Console.WriteLine($"onPublish speed: {speed}, positon: {position}");

            port.Write(new byte[] { 3, position, speed }, 0, 3);
        }

        private static byte ResolveSpeed(float interval, int direction)
        {
            // Convert back to stroke speed from the interval.
            //
            // Front and back stroke speed is asymmetric.
            // (From back to front is slower than front to back.)
            //
            byte speed = 10; // defaults. minimum.

            if (interval <= 0.1f)
            {
                speed = 60;
            }
            else if (interval <= 0.12f)
            {
                speed = 55;

            }
            else if (interval <= 0.2f)
            {
                speed = 50;
            }
            else if (interval <= 0.3f)
            {
                speed = direction > 0 ? (byte) 20 : (byte) 30;
            }
            else if (interval <= 0.55f)
            {
                speed = direction > 0 ? (byte) 15 : (byte) 20;
            }
            else if (interval <= 0.70f)
            {
                speed = direction > 0 ? (byte) 13 : (byte) 20;
            }
            else
            {
                speed = 10;
            }

            return speed;
        }

        public static void Close()
        {
            port.Close();
        }
    }
}
