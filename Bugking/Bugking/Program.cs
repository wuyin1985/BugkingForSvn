using System.Diagnostics;
using System.Globalization;
using System.Text;
using CommandLine;
namespace Bugking;

class Program
{
    private static HashSet<string> _ignoreAuthors = ["mlsvn_builder"];

    public class Options
    {
        [Option('u', "Url", Required = true, HelpText = "svn url")]
        public string url { get; set; }

        [Option('d', "StartDate", Required = true, HelpText = "Start date")]
        public string startDate { get; set; }

        [Option('t', "Time", Required = true, HelpText = "Start and end time span, eg: 00:00~23:59")]
        public string timeSpan { get; set; }

        [Option('a', "Author", Required = false, HelpText = "Filter for author")]
        public string author { get; set; }

        [Option('m', "Mode", Required = false, HelpText = "Mode: rank, detail ")]
        public string mode { get; set; }
    }


    private class SvnCommit
    {
        public string author;
        public DateTime date;
        public string message;
        public List<string> files;
    }

    private static DateTime TryParseDate(string date)
    {
        var dateStr = date;
        var idx = dateStr.IndexOf('(');
        dateStr = dateStr.Substring(0, idx - 1);
        string format = "yyyy-MM-dd HH:mm:ss zzz";
        var dateTime = DateTime.ParseExact(dateStr, format, CultureInfo.InvariantCulture);
        return dateTime;
    }

    private static SvnCommit? ReadSvnCommit(string[] logs, ref int index)
    {
        if (index >= logs.Length)
        {
            return null;
        }
        var c = new SvnCommit();
        const string logBegin = "------------------------------------------------------------------------";
        if (logs[index++] != logBegin)
        {
            throw new Exception($"error log begin {logs[index]}({logs[index].Length}) -> {logBegin.Length}");
        }
        if (index >= logs.Length)
        {
            return null;
        }
        var infoLine = logs[index++];
        var infos = infoLine.Split('|', StringSplitOptions.TrimEntries);
        c.author = infos[1];
        c.date = TryParseDate(infos[2]);

        var changePathTile = logs[index++];
        if (changePathTile != "Changed paths:")
        {
            throw new Exception($"not changed path : {changePathTile}");
        }
        int count = 0;
        var list = new List<string>();
        while(true)
        {
            var fileLine = logs[index++].Trim();
            if (fileLine == "")
            {
                break;
            }

            if (fileLine == logBegin)
            {
                throw new Exception("error log begin");
            }

            list.Add(fileLine);
            count++;
            if (count > 9999)
            {
                break;
            }
        }

        c.message = logs[index++];
        c.files = list;
        return c;
    }

    private static List<SvnCommit> DecodeLog(string allLog)
    {
        var lines = allLog.Split('\n').Select(s => s.Trim('\n', '\r')).ToArray();
        int lineIdx = 0;
        var list = new List<SvnCommit>();
        while(true)
        {
            SvnCommit? commit = null;
            try
            {
                commit = ReadSvnCommit(lines, ref lineIdx);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            if (commit == null)
            {
                break;
            }
            list.Add(commit);
        }
        return list;
    }

    private static bool IsDateTimeInTimeRange(DateTime dateTime, (TimeSpan, TimeSpan) timeSpan)
    {
        TimeSpan targetTime = new TimeSpan(dateTime.Hour, dateTime.Minute, 0);

        var startTime = timeSpan.Item1;
        var endTime = timeSpan.Item2;
        if (startTime <= endTime)
        {
            return targetTime >= startTime && targetTime <= endTime;
        }
        // 处理跨天的情况，例如22:00 - 04:00
        return targetTime >= startTime || targetTime <= endTime;
    }

    private static List<SvnCommit> GetSvnCommits(string url, DateTime startDate, (TimeSpan, TimeSpan) timeSpan, bool needVerbose)
    {
        var ret = new List<SvnCommit>();
        var dateStr = startDate.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var verbose = needVerbose ? "--verbose" : "";
        var svnLogCommand = $"log {url} {verbose} -r {{{dateStr}}}:HEAD";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "svn",
                Arguments = svnLogCommand,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process process = Process.Start(startInfo);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                output = output.Trim();
                var logs = DecodeLog(output);
                for (int i = 0; i < logs.Count; i++)
                {
                    var log = logs[i];
                    var d = log.date;
                    if (!_ignoreAuthors.Contains(log.author) && IsDateTimeInTimeRange(d, timeSpan))
                    {
                        ret.Add(log);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }

        return ret;
    }


    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                string[] parts = o.timeSpan.Split('~', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException("Invalid time range format. Expected 'HH:mm - HH:mm'");
                }

                var startDate = DateTime.Parse(o.startDate);
                var startTime = TimeSpan.Parse(parts[0]);
                var endTime = TimeSpan.Parse(parts[1]);
                var list = GetSvnCommits(o.url, startDate, (startTime, endTime), true);
                if (o.mode == "rank")
                {
                    var counter = new Dictionary<string, int>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var log = list[i];
                        counter.TryGetValue(log.author, out var count);
                        count++;
                        counter[log.author] = count;
                    }
                    var sortedDict = counter.OrderBy(pair => pair.Value);
                    foreach (var iter in sortedDict)
                    {
                        Console.WriteLine($"{iter.Key}: {iter.Value}");
                    }
                }
                else if (o.mode == "detail")
                {
                    var author = o.author?.Trim() ?? "";
                    if (string.IsNullOrEmpty(author))
                    {
                        throw new Exception("please set author to view detail");
                    }

                    var sb = new StringBuilder();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var log = list[i];
                        if (log.author == author)
                        {
                            Console.WriteLine($"{author}:");
                            Console.WriteLine($"{log.date} {log.message}");

                            for (int j = 0; j < log.files.Count; j++)
                            {
                                sb.Append(log.files[j]);
                            }
                            Console.WriteLine(sb.ToString());
                            sb.Clear();
                        }
                    }
                }

            }).WithNotParsed(HandleParseError);
    }

    static void HandleParseError(IEnumerable<Error> errs)
    {
        foreach (var e in errs)
        {
            Console.Error.WriteLine(e);
        }
    }
}