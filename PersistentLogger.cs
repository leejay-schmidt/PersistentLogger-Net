//MIT License
//
//Copyright (c) 2016 Leejay Schmidt
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace PersistentLogger {
    
    public static class PLog {
        
        public static string LogDirectory { get; set; }
        public static string LogPath { get; set; }

        static PLog() {
            //set default log directory to personal folder
            LogDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal) + "/logging";
            //set default log path
            LogPath = "log.txt";
            _queue = new LinkedList<string>();
        }

        public static void Info(string tag, string message) {
            LogMessage(tag, message, "I");
        }

        public static void Debug(string tag, string message) {
            LogMessage(tag, message, "D");
        }

        public static void Warn(string tag, string message) {
            LogMessage(tag, message, "W");
        }

        public static void Error(string tag, string message) {
            LogMessage(tag, message, "E");
        }

        public static void Wtf(string tag, string message) {
            LogMessage(tag, message, "F");
        }
        
        static void LogMessage(string tag, string message, string leadingChar) {
            string dateString = DateTime.Now.ToString("yyyy/MM/dd H:mm:ss.fff");
            string logString = dateString + "\t" + leadingChar + "/" + tag + "\t" + message;
            LogWritingManager.QueueLog(logString, LogDirectory, LogPath);
        }

    }

    static class LogWritingManager {
        static Queue _queue;
        static object _writeLock;
        //if a timeout occurs in writing logs out, determine if exception will be thrown
        public static bool ThrowIfTimeout { get; set; }
        //determines the timeout period for waiting on the available writer
        public static int TimeoutPeriod { get; set; }
        //determine if the message should be skipped or a retry should occur post-timeout
        //if a throw is not happening
        public static bool RetryIfTimeout { get; set; }
        static LogWritingManager() {
            _queue = new Queue();
            _writeLock = new object();
            TimeoutPeriod = 1000;
            RetryIfTimeout = false;
        }
        public static void QueueLog(string logString, string writeDir, string writePath) {
            //guard from null strings
            if (logString == null || writeDir == null || writePath == null) {
                return;
            }
            lock (_queue) {
                _queue.Enqueue(logString);
            }
            Threadpool.QueueUserWorkItem(o => Write(writeDir, writePath));
        }
        static string DequeueLog() {
            string log;
            lock (_queue) {
                try {
                    log = (string)_queue.Dequeue();
                } catch {
                    log = "Error Getting Log Message";
                }
            }
            return log;
        }
        static void WriteLog(string writeDir, string writePath) {
            //wait on the write lock for 1 second then time out
            //this will ensure that the logger does not hang on
            //a bad write
            if (!Monitor.TryEnter(_writeLock), 1000) {
                if (ThrowIfTimeout) {
                    throw new TimeoutException("Failed to write log message");
                } else {
                    //queue a retry. This will free up this thread and try
                    //again later on a different thread
                    if (RetryIfTimeout { get; set; }) {
                        Threadpool.QueueUserWorkItem(o => Write(writeDir, writePath);
                    } else {
                        //remove the log message if it failed and not retrying
                        //this will ensure that there are no more orphaned log messages
                        DequeueLog();
                    }
                }
            } else {
                try {
                    string logMessage = DequeueLog();
                    if (logMessage != null) {
                        if (!Directory.Exists(writeDir)) {
                            Directory.CreateDirectory(writeDir);
                        }
                        string filePath = Path.Combine(writeDir, writePath);
                        if (!File.Exists(filePath)) {
                            using (StreamWriter sw = File.CreateText(filePath)) {
                                sw.WriteLine("--------Start of Persistent Log File--------");
                            }
                        }

                        using (StreamWriter sw = File.AppendText(filePath)) {
                            sw.WriteLine(logMessage);
                        }
                    }
                } finally {
                    Monitor.Exit(_writeLock);
                }
            }
        }        
    }
}
