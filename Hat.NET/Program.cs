﻿using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

namespace Hat.NET
{

    public class HttpProcessor
    {
        public TcpClient socket;
        public HttpServer srv;

        private Stream inputStream;
        public StreamWriter outputStream;

        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv)
        {
            this.socket = s;
            this.srv = srv;
        }


        private string streamReadLine(Stream inputStream)
        {
            int next_char;
            string data = "";
            while (true)
            {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }
            return data;
        }
        public void process()
        {
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());

            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try
            {
                parseRequest();
                readHeaders();
                if (http_method.Equals("GET"))
                {
                    handleGETRequest();
                }
                else if (http_method.Equals("POST"))
                {
                    handlePOSTRequest();
                }
            }
            catch (Exception e)
            {
                Logger.Log("Exception: " + e.ToString());
                writeFailure();
            }
            try
            {
                outputStream.Flush();
            }
            catch {  }
            // bs.Flush(); // flush any remaining output
            inputStream = null; outputStream = null; // bs = null;            
            socket.Close();
        }

        public void parseRequest()
        {
            string request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3)
            {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Logger.Log("starting: " + request);
        }

        public void readHeaders()
        {
            Logger.Log("readHeaders()");
            string line;
            while ((line = streamReadLine(inputStream)) != null)
            {
                if (line.Equals(""))
                {
                    Logger.Log("got headers");
                    return;
                }

                int separator = line.IndexOf(':');
                if (separator == -1)
                {
                    throw new Exception("invalid http header line: " + line);
                }
                string name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' '))
                {
                    pos++; // strip any spaces
                }

                string value = line.Substring(pos, line.Length - pos);
                Logger.Log(string.Format("header: {0}:{1}", name, value));
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest()
        {
            Console.WriteLine("GET");
            srv.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest()
        {
            // this post data processing just reads everything into a memory stream.
            // this is fine for smallish things, but for large stuff we should really
            // hand an input stream to the request processor. However, the input stream 
            // we hand him needs to let him see the "end of the stream" at this content 
            // length, because otherwise he won't know when he's seen it all! 

            Logger.Log("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length"))
            {
                content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                if (content_len > MAX_POST_SIZE)
                {
                    throw new Exception(
                        String.Format("POST Content-Length({0}) too big for this simple server",
                          content_len));
                }
                byte[] buf = new byte[BUF_SIZE];
                int to_read = content_len;
                while (to_read > 0)
                {
                    Logger.Log("starting Read, to_read={0}", to_read);

                    int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                    Logger.Log("read finished, numread={0}", numread);
                    if (numread == 0)
                    {
                        if (to_read == 0)
                        {
                            break;
                        }
                        else
                        {
                            throw new Exception("client disconnected during post");
                        }
                    }
                    to_read -= numread;
                    ms.Write(buf, 0, numread);
                }
                ms.Seek(0, SeekOrigin.Begin);
            }
            Logger.Log("get post data end");
            Logger.Log("POST");
            srv.handlePOSTRequest(this, new StreamReader(ms));

        }

        public void writeSuccess(string content_type = "text/html")
        {
            // this is the successful HTTP response line
            outputStream.WriteLine("HTTP/1.0 200 OK");
            // these are the HTTP headers...          
            outputStream.WriteLine("Content-Type: " + content_type);
            outputStream.WriteLine("Connection: close");
            // ..add your own headers here if you like

            outputStream.WriteLine(""); // this terminates the HTTP headers.. everything after this is HTTP body..
        }

        public void writeFailure()
        {
            // this is an http 404 failure response
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            // these are the HTTP headers
            outputStream.WriteLine("Connection: close");
            // ..add your own headers here

            outputStream.WriteLine(""); // this terminates the HTTP headers.
        }
    }

    public class HttpServer
    {

        protected int port;
        TcpListener listener;
        bool is_active = true;

        public HttpServer(int port)
        {
            this.port = port;
        }

        public void listen()
        {
            listener = new TcpListener(port);
            listener.Start();
            while (is_active)
            {
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public void handleGETRequest(HttpProcessor p)
        {
            Logger.Log(string.Format("request: {0}", p.http_url));
            Logger.Log(Path.GetExtension(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Length == 1 ? "index" : p.http_url.Substring(1))));
            if(p.http_url.Replace("/", "") == "console")
            {
                p.writeSuccess("text/plain");
                Logger.ViewLog(p.outputStream);
                p.outputStream.Flush();
                Logger.SaveLog();
                return;
            }
            //If file doesn't exist try trying it as a folder
            if(Directory.Exists(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Length == 1 ? "" : p.http_url.Substring(1))) && !File.Exists(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Length == 1 ? "" : p.http_url.Substring(1))))
            {
                p.http_url += "/";
            }
            //if everything else fails, try 404
            else if(!Directory.Exists(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Length == 1 ? "" : p.http_url.Substring(1))) && !File.Exists(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Length == 1 ? "" : p.http_url.Substring(1))))
            {
                handle404(p);
                return;
            }
            if(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url).EndsWith("/"))
            {
                Logger.Log(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url));
                string[] files = Directory.GetFiles(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), (p.http_url.Length == 1 ? "" : p.http_url.Substring(1))));
                bool flag = false;
                foreach(string filename in files)
                {
                    if(Path.GetFileName(filename).Contains("index"))
                    {
                        switch (Path.GetExtension(filename))
                        {
                            case ".png":
                                Console.WriteLine("picture");
                                Console.WriteLine(filename);
                                Stream fs = File.Open(filename, FileMode.Open);
                                p.writeSuccess("image/png");
                                fs.CopyTo(p.outputStream.BaseStream);
                                p.outputStream.BaseStream.Flush();
                                flag = true;
                                break;
                            case ".wc":
                                WCParser.Parse(File.ReadAllText(filename), p.outputStream);
                                flag = true;
                                break;
                            case ".css":
                                flag = true;
                                p.writeSuccess("text/css");
                                p.outputStream.Write(File.ReadAllText(filename));
                                p.outputStream.Flush();
                                break;
                            default:
                                flag = true;
                                p.writeSuccess();
                                p.outputStream.Write(File.ReadAllText(filename));
                                p.outputStream.Flush();
                                break;
                        }
                        break;
                    }
                }
                if (!flag)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("<a href=\"../\">../</a><br>");
                    foreach(string file in files)
                    {
                        sb.AppendLine(string.Format("<a href=\"{0}\">{0}</a><br>", file.Replace(Path.Combine(Environment.CurrentDirectory, "server"), "")));
                    }
                    p.outputStream.WriteLine(sb.ToString());
                    p.writeSuccess();
                    p.outputStream.Flush();
                    return;
                }
                return;
            }
            switch (Path.GetExtension(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Substring(1))))
            {
                case ".png":
                    Logger.Log("picture");
                    Logger.Log(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Substring(1)));
                    Stream fs = File.Open(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Substring(1)), FileMode.Open);
                    p.writeSuccess("image/png");
                    fs.CopyTo(p.outputStream.BaseStream);
                    p.outputStream.BaseStream.Flush();
                    break;
                case ".wc":
                    WCParser.Parse(File.ReadAllText(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Substring(1))), p.outputStream);
                    break;
                case ".css":
                    p.writeSuccess("text/css");
                    p.outputStream.Write(File.ReadAllText(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Substring(1))));
                    p.outputStream.Flush();
                    break;
                default:
                    p.writeSuccess("text/plain");
                    p.outputStream.Write(File.ReadAllText(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), p.http_url.Substring(1))));
                    p.outputStream.Flush();
                    break;
            }
            Logger.SaveLog();
        }
        public void handlePOSTRequest(HttpProcessor p, StreamReader inputData)
        {
            Console.WriteLine(string.Format("POST request: {0}", p.http_url));
            string data = inputData.ReadToEnd();

            p.writeSuccess();
            p.outputStream.WriteLine("<html><body><h1>test server</h1>");
            p.outputStream.WriteLine("<a href=/test>return</a><p>");
            p.outputStream.WriteLine("postbody: <pre>{0}</pre>", data);
        }

        public static void handle404(HttpProcessor p)
        {
            if (File.Exists(Path.Combine(Environment.CurrentDirectory, "server/404")))
            {
                string fs = File.ReadAllText(Path.Combine(Path.Combine(Environment.CurrentDirectory, "server"), "404"));
                p.writeSuccess();
                p.outputStream.WriteLine(fs);
                p.outputStream.Flush();
            }
            else
            {
                p.writeSuccess();
                p.outputStream.WriteLine("<h1>404 404 - 404 Not found</h1>".WriteHTMLStub());
                p.outputStream.Flush();
            }
        }
    }
    public class Program
    {
        public static bool Talkative = false;
        public static event EventHandler<HandledEventArgs> Exit = delegate { };
        public static int Main(string[] args)
        {
            HttpServer httpServer;
            if(!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "server")))
            {
                Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "server"));
            }
            if(!File.Exists(Path.Combine(Environment.CurrentDirectory, "server/404")))
            {
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "server/404"), "<h1>404 - Not found</h1><br><h5>__________________________________________________________________<br>Hat.NET - an opensource .NET webserver software</h5>".WriteHTMLStub());
            }
            if (args.GetLength(0) > 0)
            {
                httpServer = new HttpServer(Convert.ToInt16(args[0]));
                Console.WriteLine("Listening on port ", args[0]);
            }
            else
            {
                httpServer = new HttpServer(8080);
                Console.WriteLine("Listening on port 8080");
            }
            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            return 0;
        }

    }

}