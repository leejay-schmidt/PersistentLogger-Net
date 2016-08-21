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
using System.Collections;
using System.Threading;

namespace PersistentLogger {
    
    public static class PLog {
        
        // the directory in which to save the log file
        public static string LogDirectory { get; set; }

        // the actual file path of the log file (without directory)
        public static string LogPath { get; set; }

        static PLog() {
            // set default log directory to personal folder
            LogDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + "/logging";
            // set default log path
            LogPath = "log.txt";
        }
        
        /// <summary>
        /// Appends a log message marked "I" into the log file specified.
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
            // get the string with the date and time details to timestamp the log message
            string dateString = DateTime.Now.ToString("yyyy/MM/dd H:mm:ss.fff");
            // format the message string (format is TIMESTAMP LC/TAG   MESSAGE)
            string logString = dateString + "\t" + leadingChar + "/" + tag + "\t" + message;
            // pass the message off to the log writing manager to handle from here
            LogWritingManager.QueueLog(logString, LogDirectory, LogPath);
        }

    }

    static class LogWritingManager {

        // used to buffer messages until file writer is able to deal with them
        static Queue _queue;
        
        // used to prevent concurrent writes to file 
        static readonly object _writeLock;

        // if a timeout occurs in writing logs out, determine if exception will be thrown
        public static bool ThrowIfTimeout { get; set; }

        // determines the timeout period for waiting on the available writer
        public static int TimeoutPeriod { get; set; }

        // determine if the message should be skipped or a retry should occur post-timeout
        // if a throw is not happening
        public static bool RetryIfTimeout { get; set; }

        static LogWritingManager() {
            _queue = new Queue();
            _writeLock = new object();
            // use a default timeout of one second to prevent total hangs of the application
            TimeoutPeriod = 1000;
            // by default, if the file writer wait times out, just drop that message and move
            // on, in order to preven the queue from piling up
            RetryIfTimeout = false;
        }

        public static void QueueLog(string logString, string writeDir, string writePath) {
            // guard from null strings
            if (logString == null || writeDir == null || writePath == null) {
                return;
            }
            // in order to prevent concurrent reads/writes from the queue, lock it while adding
            lock (_queue) {
                // queue up the log message string
                _queue.Enqueue(logString);
            }
            // add the write to the thread pool. Since the messages were queued in a synchronised
            // way, it won't matter what order they are executed in (except if the write directory
            // or write path changed)
            ThreadPool.QueueUserWorkItem(o => WriteLog(writeDir, writePath));
        }

        static string DequeueLog() {
            // this is the log message that will get returned
            // must be declared outside of the try/catch and lock for visibility
            string log;
            // lock on the queue to prevent concurrent reads and writes from it
            lock (_queue) {
                try {
                    // try to get a string from the queue. If this is not possible, 
                    // and exception will be thrown
                    log = (string)_queue.Dequeue();
                } catch {
                    // in case retrieving the log message failed, log this message instead
                    log = "Error Getting Log Message";
                }
            }
            // return the next available log message (if applicable)
            return log;
        }

        static void WriteLog(string writeDir, string writePath) {
            // wait on the write lock for 1 second then time out
            // this will ensure that the logger does not hang on
            // a bad write
            if (!Monitor.TryEnter(_writeLock, TimeoutPeriod)) {
                if (ThrowIfTimeout) {
                    // if the user chose to throw on a timeout, then do this
                    throw new TimeoutException("Failed to write log message");
                } else {
                    //queue a retry. This will free up this thread and try
                    //again later on a different thread
                    if (RetryIfTimeout) {
                        ThreadPool.QueueUserWorkItem(o => WriteLog(writeDir, writePath));
                    } else {
                        //remove the log message if it failed and not retrying
                        //this will ensure that there are no more orphaned log messages
                        DequeueLog();
                    }
                }
            } else {
                // ensure that the file manager issues will be caught
                try {
                    // get the next message from the queue
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
                    // ensure that the monitor always exits, even on error
                    Monitor.Exit(_writeLock);
                }
            }
        }        
    }
}
