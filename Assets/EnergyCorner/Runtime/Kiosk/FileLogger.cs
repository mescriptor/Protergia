using System;
using System.IO;
using UnityEngine;

namespace EnergyCorner
{
    /// <summary>
    /// Writes every Unity log message to a rotating file:
    ///   %USERPROFILE%\AppData\LocalLow\&lt;Company&gt;\&lt;Product&gt;\Logs\energycorner.log
    /// (= Application.persistentDataPath/Logs). When the file reaches logMaxKB it becomes
    /// energycorner.1.log, the old .1 becomes .2, and so on; logFilesToKeep files are kept.
    /// Starts automatically before the first scene loads; no component needed.
    /// </summary>
    public static class FileLogger
    {
        static readonly object Lock = new object();
        static StreamWriter writer;
        static string path;
        static long maxBytes;
        static int keep;

        public static string LogPath => path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.quitting -= Close;
            Close();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (writer != null) return;
            try
            {
                var cfg = EnergyCornerConfig.Current;
                maxBytes = Math.Max(64, cfg.logMaxKB) * 1024L;
                keep = Math.Max(1, cfg.logFilesToKeep);

                string dir = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "energycorner.log");
                OpenWriter();

                Application.logMessageReceivedThreaded += OnLog;
                Application.quitting += Close;
                Write("INFO", "===== Energy Corner started (" + Application.version + ") =====");
            }
            catch (Exception e)
            {
                Debug.LogError("[EC] File logging disabled: " + e.Message);
            }
        }

        static void OpenWriter()
        {
            writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            string level = type == LogType.Log ? "INFO" : type == LogType.Warning ? "WARN" : "ERROR";
            bool withStack = type == LogType.Exception || type == LogType.Error;
            Write(level, withStack && !string.IsNullOrEmpty(stackTrace) ? message + "\n" + stackTrace : message);
        }

        static void Write(string level, string message)
        {
            lock (Lock)
            {
                if (writer == null) return;
                try
                {
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + message);
                    if (writer.BaseStream.Length > maxBytes) Rotate();
                }
                catch (Exception)
                {
                    // Never let logging crash the app (disk full, file locked...).
                    // If a rotation failed half-way, keep appending to the main file.
                    if (writer == null)
                    {
                        try { OpenWriter(); } catch (Exception) { }
                    }
                }
            }
        }

        static void Rotate()
        {
            writer.Dispose();
            writer = null;
            string Numbered(int i) => Path.ChangeExtension(path, null) + "." + i + ".log";

            string oldest = Numbered(keep - 1);
            if (keep == 1) { File.Delete(path); }
            else
            {
                if (File.Exists(oldest)) File.Delete(oldest);
                for (int i = keep - 2; i >= 1; i--)
                    if (File.Exists(Numbered(i))) File.Move(Numbered(i), Numbered(i + 1));
                File.Move(path, Numbered(1));
            }
            OpenWriter();
        }

        static void Close()
        {
            lock (Lock)
            {
                if (writer == null) return;
                try { writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " INFO ===== quit ====="); }
                catch (Exception) { }
                finally
                {
                    try { writer.Dispose(); } catch (Exception) { }
                    writer = null;
                }
            }
        }
    }
}
