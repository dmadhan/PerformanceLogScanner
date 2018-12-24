using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp5
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> list = new List<string>();
            Dictionary<Guid, List<SQLStageTime>> connDictionary = new Dictionary<Guid, List<SQLStageTime>>();
            List<NotClosedNotDisposed> opeNotClosedNotDisposed = new List<NotClosedNotDisposed>();
            List<NotClosed> openNotClosed = new List<NotClosed>();
            List<NotDisposed> openNotDisposed = new List<NotDisposed>();
            List<ConnectionTimers> connectionTimers = new List<ConnectionTimers>();
            string filePath = args[0];
            string format = args[1];
            bool milliSeconds = format == "ms" ? true : false;
            using (StreamReader sr = File.OpenText(filePath))
            {
                string TimeStamp = "";
                string stage = "";
                string Id = "";
                string Hash = "";
                string root = "";
                string origin = "";
                string s = "";
                bool blockEnded = false;
                while ((s = sr.ReadLine()) != null)
                {
                    if (s.Contains("INFO Performance -"))
                    {
                        TimeStamp = s.Substring(0, s.IndexOf('[') - 1);
                    }
                    else if (s.Contains("Stage -"))
                    {
                        stage = s.Substring(s.IndexOf("Stage - ") + 8);
                    }
                    else if (s.Contains("Id -"))
                    {
                        Id = s.Substring(s.IndexOf("Id - ") + 5);
                    }
                    else if (s.Contains("Hash -"))
                    {
                        Hash = s.Substring((s.IndexOf("Hash - ") + 7));
                        //blockEnded = true;
                    }
                    else if (s.Contains("Root -"))
                    {
                        root = s.Substring((s.IndexOf("Root - ") + 7));
                        //blockEnded = true;
                    }
                    else if (!string.IsNullOrEmpty(root) && s.Contains(root))
                    {
                        origin = s;
                        blockEnded = true;
                    }

                    if (blockEnded)
                    {
                        Guid identifier = Guid.Empty;
                        if (Guid.TryParse(Id, out identifier))
                        {
                            if (connDictionary.ContainsKey(identifier))
                            {
                                int order = connDictionary[identifier].OrderBy(m => m.Order).Last().Order;
                                connDictionary[identifier].Add(new SQLStageTime { Order = order + 1, Stage = stage, Time = DateTime.Parse(TimeStamp), Origin = origin, Root = root });
                            }
                            else
                            {
                                connDictionary.Add(identifier, new List<SQLStageTime> { new SQLStageTime { Order = 1, Stage = stage, Time = DateTime.Parse(TimeStamp), Origin = origin, Root = root } });
                            }
                        }
                        root = null;
                        blockEnded = false;
                    }

                    //list.Add(s);
                }
            }

            foreach (var ids in connDictionary)
            {
                var key = ids.Key;
                var transactions = ids.Value;

                DateTime openTime = new DateTime();
                DateTime closeTime = new DateTime();
                DateTime disposeTime = new DateTime();

                var openTransactionCount = transactions.Count(m => m.Stage == "Open Connection");
                var closedTransactionCount = transactions.Count(m => m.Stage == "Close Connection");
                var disposedTransactionCount = transactions.Count(m => m.Stage == "Dispose Connection");


                if (openTransactionCount == closedTransactionCount &&
                    closedTransactionCount == disposedTransactionCount) // Properly Managed
                {
                    foreach (var sqlStageTime in transactions.OrderBy(m => m.Order))
                    {
                        if (sqlStageTime.Stage == "Open Connection")
                        {
                            openTime = sqlStageTime.Time;
                            continue;
                        }

                        if (sqlStageTime.Stage == "Close Connection")
                        {
                            closeTime = sqlStageTime.Time;
                            continue;
                        }

                        if (sqlStageTime.Stage == "Dispose Connection")
                        {
                            disposeTime = sqlStageTime.Time;
                        }

                        connectionTimers.Add(new ConnectionTimers
                        {
                            Guid = key,
                            CloseDuration = closeTime.Subtract(openTime),
                            DisposedDuration = disposeTime.Subtract(closeTime),
                            TotalElapsedDuration = disposeTime.Subtract(openTime)
                        });

                    }
                }
                else if (openTransactionCount != closedTransactionCount && openTransactionCount != disposedTransactionCount) //open but not closed and disposed
                {
                    var stringlist = new List<string>();
                    stringlist.Add("Stage connections open but not closed and disposed");
                    foreach (var trans in transactions.OrderBy(m => m.Order))
                    {
                        stringlist.Add(trans.Stage + ":" + trans.Root + ":" + trans.Origin);
                    }

                    opeNotClosedNotDisposed.Add(new NotClosedNotDisposed
                    {
                        Guid = key,
                        Message = stringlist
                    });
                }
                else if (openTransactionCount != closedTransactionCount) //open but not closed
                {
                    var stringlist = new List<string>();
                    stringlist.Add("Stage connections open but not closed");
                    foreach (var trans in transactions.OrderBy(m => m.Order))
                    {
                        stringlist.Add(trans.Stage + ":" + trans.Root + ":" + trans.Origin);
                    }
                    openNotClosed.Add(new NotClosed
                    {
                        Guid = key,
                        Message = stringlist
                    });
                }
                else if (openTransactionCount != disposedTransactionCount) //open but not disposed
                {
                    var stringlist = new List<string>();
                    stringlist.Add("Stage connections open but not disposed");
                    foreach (var trans in transactions.OrderBy(m => m.Order))
                    {
                        stringlist.Add(trans.Stage + ":" + trans.Root + ":" + trans.Origin);
                    }
                    openNotDisposed.Add(new NotDisposed
                    {
                        Guid = key,
                        Message = stringlist
                    });
                }

            }


            Console.WriteLine("Processing connections not disposed and not closed count: " + opeNotClosedNotDisposed.Count);
            Console.WriteLine("Press Enter to continue display the stack trace");
            Console.ReadLine();
            foreach (var unMaintainedConnection in opeNotClosedNotDisposed)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("UnManaged Connections");
                Console.WriteLine(unMaintainedConnection.Guid);
                Console.ResetColor();
                foreach (var msg in unMaintainedConnection.Message)
                {
                    Console.WriteLine(msg);
                }
            }

            var consolidatedRes = opeNotClosedNotDisposed.SelectMany(m => m.Message).ToList();

            var groupedRes = from p in consolidatedRes
                             where p != "Stage connections open but not closed and disposed"
                             group p by p;

            Console.WriteLine("Consolidated messages");

            foreach (var c in groupedRes.OrderByDescending(m => m.Count()))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(c.FirstOrDefault());
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Count: " + c.Count());
            }

            Console.ResetColor();

            Console.WriteLine("Processing connections not closed count: " + openNotClosed.Count);
            Console.WriteLine("Press Enter to continue display the stack trace");
            Console.ReadLine();

            foreach (var unMaintainedConnection in openNotClosed)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Open but not closed");
                Console.WriteLine(unMaintainedConnection.Guid);
                Console.ResetColor();
                foreach (var msg in unMaintainedConnection.Message)
                {
                    Console.WriteLine(msg);
                }
            }

            var consolidatedRes2 = openNotClosed.SelectMany(m => m.Message).ToList();

            var groupedRes2 = from p in consolidatedRes2
                              where p != "Stage connections open but not closed"
                              group p by p;

            Console.WriteLine("Consolidated messages");

            foreach (var c in groupedRes2.OrderByDescending(m => m.Count()))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(c.FirstOrDefault());
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Count: " + c.Count());
            }

            Console.ResetColor();

            Console.WriteLine("Processing connections not disposed count: " + openNotDisposed.Count);
            Console.WriteLine("Press Enter to continue display the stack trace");
            Console.ReadLine();

            foreach (var unMaintainedConnection in openNotDisposed)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Open but not disposed");
                Console.WriteLine(unMaintainedConnection.Guid);
                Console.ResetColor();

                foreach (var msg in unMaintainedConnection.Message)
                {
                    Console.WriteLine(msg);
                }
            }

            var consolidatedRes3 = openNotDisposed.SelectMany(m => m.Message).ToList();

            var groupedRes3 = from p in consolidatedRes3
                where p != "Stage connections open but not disposed"
                              group p by p;

            Console.WriteLine("Consolidated messages");

            foreach (var c in groupedRes3.OrderByDescending(m => m.Count()))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(c.FirstOrDefault());
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Count: " + c.Count());
            }

            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Connection Durations");
            Console.ResetColor();

            if (milliSeconds)
            {
                foreach (var connectionTimer in connectionTimers.Where(m => m.TotalElapsedDuration.Milliseconds != 0)
                    .OrderBy(m => m.TotalElapsedDuration.Milliseconds))
                {
                    Console.WriteLine(connectionTimer.Guid);
                    Console.WriteLine("Closed Duration :" + connectionTimer.CloseDuration.Milliseconds + "ms");
                    Console.WriteLine("Disposed Duration :" + connectionTimer.DisposedDuration.Milliseconds + "ms");
                    Console.WriteLine("Elapsed Duration :" + connectionTimer.TotalElapsedDuration.Milliseconds + "ms");
                }
            }
            else
            {
                foreach (var connectionTimer in connectionTimers.Where(m => m.TotalElapsedDuration.Seconds != 0)
                    .OrderBy(m => m.TotalElapsedDuration.Seconds))
                {
                    Console.WriteLine(connectionTimer.Guid);
                    Console.WriteLine("Closed Duration :" + connectionTimer.CloseDuration.Seconds +"sec");
                    Console.WriteLine("Disposed Duration :" + connectionTimer.DisposedDuration.Seconds + "sec");
                    Console.WriteLine("Elapsed Duration :" + connectionTimer.TotalElapsedDuration.Seconds + "sec");
                }
            }

            Console.WriteLine("All Done.......Continue.......");
            Console.ReadLine();

        }
    }

    class SQLStageTime
    {
        public int Order { get; set; }
        public string Stage { get; set; }
        public DateTime Time { get; set; }
        public string Root { get; set; }
        public string Origin { get; set; }
    }

    class NotClosedNotDisposed
    {
        public NotClosedNotDisposed()
        {
            Message = new List<string>();
        }
        public Guid Guid { get; set; }
        public List<string> Message { get; set; }
    }

    class NotClosed
    {
        public NotClosed()
        {
            Message = new List<string>();
        }
        public Guid Guid { get; set; }
        public List<string> Message { get; set; }
    }

    class NotDisposed
    {
        public NotDisposed()
        {
            Message = new List<string>();
        }
        public Guid Guid { get; set; }
        public List<string> Message { get; set; }
    }

    class ConnectionTimers
    {
        public Guid Guid { get; set; }
        public TimeSpan CloseDuration { get; set; }
        public TimeSpan DisposedDuration { get; set; }
        public TimeSpan TotalElapsedDuration { get; set; }
    }
}
