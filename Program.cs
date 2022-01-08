using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QQChatRecordsToTable
{
    public class Program
    {
        public const string ConfigFileName = "config.txt";
        public const string SplitParttern = "================================================================";
        public const string RecentGroupName = "最近联系人";
        public static readonly Regex TimeR = new Regex(@"^(?<time>\w{4}\-\w{2}\-\w{2} [0-2]?\w:\w{2}:\w{2}) (?<name>.+)", RegexOptions.Compiled);

        public static string Input { get; set; } = "全部消息记录.txt";
        public static string OutputDir { get; set; } = "output";
        public static string OutputFileName { get; set; } = "{Group}-{Name}.csv";
        public static bool IgnoreEmpty { get; set; } = false;
        public static bool IgnoreRecent { get; set; } = true;
        public static bool MultiLines { get; set; } = true;

        public static void Main(string[] args)
        {
            try
            {
                InitializeConfigs();
                var chats = ParseRecords();
                WriteToCsv(chats);
                Process.Start(new ProcessStartInfo()
                {
                    FileName = "explorer.exe",
                    Arguments = Path.GetFullPath(OutputDir),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("发生错误");
                Console.Error.WriteLine(ex.ToString());
                Console.ReadKey();
            }
        }

        public static void InitializeConfigs()
        {
            if (!File.Exists(ConfigFileName))
            {
                return;
            }

            var lines = File.ReadAllLines(ConfigFileName).Where(p => !string.IsNullOrWhiteSpace(p));
            foreach (var line in lines)
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    switch (parts[0])
                    {
                        case nameof(Input):
                            Input = parts[1];
                            break;

                        case nameof(OutputDir):
                            OutputDir = parts[1];
                            break;

                        case nameof(OutputFileName):
                            OutputFileName = parts[1];
                            break;

                        case nameof(IgnoreEmpty):
                            IgnoreEmpty = bool.Parse(parts[1]);
                            break;

                        case nameof(IgnoreRecent):
                            IgnoreRecent = bool.Parse(parts[1]);
                            break;

                        case nameof(MultiLines):
                            MultiLines = bool.Parse(parts[1]);
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        public static List<Chat> ParseRecords()
        {
            int lineIndex = 0;
            int splitCount = 0;
            string group = "";
            string objName = "";
            Message message = null;
            List<Message> messages = new List<Message>();
            List<Chat> chats = new List<Chat>();

            foreach (var line in File.ReadLines(Input).Skip(2))
            {
                lineIndex++;

                #region 分隔符检查

                if (line == SplitParttern)
                {
                    if (splitCount == 0)
                    {
                        if (message != null)
                        {
                            messages.Add(message);
                        }
                        if (messages.Count != 0)
                        {
                            chats.Add(new Chat()
                            {
                                Group = group,
                                Name = objName,
                                Messages = messages
                            });
                            Console.WriteLine($"正在解析{group}-{objName}");
                            messages = new List<Message>();
                            message = null;
                        }
                    }

                    splitCount = (splitCount + 1) % 3;
                    continue;
                }
                if (splitCount == 1)
                {
                    if (line.StartsWith("消息分组:"))
                    {
                        group = line[5..];
                        continue;
                    }
                    Console.WriteLine($"第{lineIndex}错误，应为消息分组，但为{line}");
                }
                if (splitCount == 2)
                {
                    if (line.StartsWith("消息对象:"))
                    {
                        objName = line[5..];
                        continue;
                    }
                    Console.WriteLine($"第{lineIndex}错误，应为消息对象，但为{line}");
                }

                #endregion 分隔符检查

                //此时splitCount==0
                if (TimeR.IsMatch(line))
                {
                    var match = TimeR.Match(line);
                    string timeStr = match.Groups["time"].Value;
                    if (message != null)
                    {
                        messages.Add(message);
                    }
                    message = new Message()
                    {
                        Time = DateTime.Parse(timeStr),
                        Name = match.Groups["name"].Value
                    };
                    continue;
                }
                if (message != null)
                {
                    message.AppendLine(line);
                }
            }

            return chats;
        }

        public static void WriteToCsv(List<Chat> chats)
        {
            if (Directory.Exists(OutputDir))
            {
                Console.WriteLine($"正在删除目录");
                Directory.Delete(OutputDir, true);
            }
            Directory.CreateDirectory(OutputDir);

            foreach (var chat in chats)
            {
                if (IgnoreRecent && chat.Group == RecentGroupName)
                {
                    continue;
                }
                Console.WriteLine($"正在写入{chat.Group}-{chat.Name}");
                string path = Path.Combine(OutputDir, GetLegalName(OutputFileName.Replace("{Group}", chat.Group).Replace("{Name}", chat.Name)));
                CsvConfiguration config = new CsvConfiguration(CultureInfo.CurrentCulture);
                //config.NewLine = Environment.NewLine;
                using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
                using (var csv = new CsvWriter(writer, config))
                {
                    csv.Context.RegisterClassMap<MessageMap>();
                    var messages = chat.Messages;
                    if (IgnoreEmpty)
                    {
                        messages = chat.Messages.Where(p => !string.IsNullOrEmpty(p.Content)).ToList();
                    }
                    csv.WriteRecords(chat.Messages);
                }
            }
        }

        public static string GetLegalName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '-');
            }
            return name;
        }
    }

    public class MessageMap : ClassMap<Message>
    {
        public MessageMap()
        {
            Map(p => p.Time).Index(0).Name("时间");
            Map(p => p.Name).Index(0).Name("发送者");
            Map(p => p.Content).Index(0).Name("内容");
        }
    }

    public class Message
    {
        public DateTime Time { get; set; }
        public string Name { get; set; }
        private StringBuilder str = new StringBuilder();

        public void AppendLine(string content)
        {
            if (Program.MultiLines)
            {
                str.AppendLine(content);
            }
            else
            {
                str.Append(content);
            }
        }

        public string Content
        {
            get
            {
                while (str.Length > 0 && (str[^1] == '\r' || str[^1] == '\n'))
                {
                    str.Remove(str.Length - 1, 1);
                }
                return str.ToString();
            }
        }
    }

    public class Chat
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public List<Message> Messages { get; set; }
    }
}