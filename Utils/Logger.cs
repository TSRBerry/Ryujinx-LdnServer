using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LanPlayServer.Utils;

// A very simple logger
public class Logger : IDisposable
{
    private static Logger _instance;
    public static Logger Instance {
        get
        {
            _instance ??= new Logger();

            return _instance;
        }
    }

    private readonly ConcurrentQueue<(bool, string, bool)> _messageQueue = new();
    private readonly AutoResetEvent _messageReady = new(false);
    private readonly Thread _consumerThread;
    private bool _active = true;

    private readonly StreamWriter _writer;

    private Logger()
    {
        var fileStream = File.Open("/tmp/Ryujinx-LdnServer.log", FileMode.Append, FileAccess.Write);
        _writer = new StreamWriter(fileStream);

        _consumerThread = new Thread(ConsumeMessages)
        {
            Name = "LoggerThread"
        };
        _consumerThread.Start();
    }

    // Debug does not log to console
    public void Debug(string className, string message, bool newline = true, [CallerMemberName] string member = "")
    {
        _messageQueue.Enqueue((false, $"{DateTime.Now} |D| ({Thread.CurrentThread.Name}) [{className}.{member}] {message}", newline));
        _messageReady.Set();
    }

    // Info logs to console
    public void Info(string className, string message, bool newline = true, [CallerMemberName] string member = "")
    {
        _messageQueue.Enqueue((true, $"{DateTime.Now} |I| ({Thread.CurrentThread.Name}) [{className}.{member}] {message}", newline));
        _messageReady.Set();
    }

    // Warning logs to console
    public void Warning(string className, string message, bool newline = true, [CallerMemberName] string member = "")
    {
        _messageQueue.Enqueue((true, $"{DateTime.Now} |W| ({Thread.CurrentThread.Name}) [{className}.{member}] {message}", newline));
        _messageReady.Set();
    }

    // Error logs to console
    public void Error(string className, string message, bool newline = true, [CallerMemberName] string member = "")
    {
        _messageQueue.Enqueue((true, $"{DateTime.Now} |E| ({Thread.CurrentThread.Name}) [{className}.{member}] {message}", newline));
        _messageReady.Set();
    }

    private void ConsumeMessages()
    {
        while (_active)
        {
            _messageReady.WaitOne();

            while (_messageQueue.TryDequeue(out var result))
            {
                try
                {
                    // Use WriteLine
                    if (result.Item3)
                    {
                        _writer.WriteLine(result.Item2);

                        // Write to console
                        if (result.Item1)
                        {
                            Console.WriteLine(result.Item2);
                        }
                    }
                    else
                    {
                        _writer.Write(result.Item2);

                        // Write to console
                        if (result.Item1)
                        {
                            Console.Write(result.Item2);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Uncaught logger message exception: {e}");
                }
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _active = false;
        _messageReady.Set();
        _consumerThread.Join();
        _writer.Close();
    }
}
