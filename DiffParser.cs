using System.Collections;
using System.Text.RegularExpressions;

namespace BeamNG.RemoteControlPatcher
{
    public class DiffParser
    {
        private delegate void ParserAction(string line, Match m);

        private class HandlerRow
        {
            public Regex Expression { get; }

            public Action<string, Match> Action { get; }

            public HandlerRow(Regex expression, Action<string, Match> action)
            {
                Expression = expression;
                Action = action;
            }
        }

        private class HandlerCollection : IEnumerable<HandlerRow>, IEnumerable
        {
            private List<HandlerRow> handlers = new List<HandlerRow>();

            public void Add(string expression, Action action)
            {
                handlers.Add(new HandlerRow(new Regex(expression), delegate
                {
                    action();
                }));
            }

            public void Add(string expression, Action<string> action)
            {
                handlers.Add(new HandlerRow(new Regex(expression), delegate (string line, Match m)
                {
                    action(line);
                }));
            }

            public void Add(string expression, Action<string, Match> action)
            {
                handlers.Add(new HandlerRow(new Regex(expression), action));
            }

            public IEnumerator<HandlerRow> GetEnumerator()
            {
                return handlers.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return handlers.GetEnumerator();
            }
        }

        private const string noeol = "\\ No newline at end of file";

        private const string devnull = "/dev/null";

        private List<FileDiff> files = new List<FileDiff>();

        private int in_del;

        private int in_add;

        private Chunk current = null!;

        private FileDiff file = null!;

        private int oldStart;

        private int newStart;

        private int oldLines;

        private int newLines;

        private readonly HandlerCollection schema;

        public DiffParser()
        {
            schema = new HandlerCollection
            {
                { "^diff\\s", Start },
                { "^new file mode \\d+$", NewFile },
                { "^deleted file mode \\d+$", DeletedFile },
                { "^_index\\s[\\da-zA-Z]+\\.\\.[\\da-zA-Z]+(\\s(\\d+))?$", Index },
                { "^---\\s", FromFile },
                { "^\\+\\+\\+\\s", ToFile },
                { "^@@\\s+\\-(\\d+),?(\\d+)?\\s+\\+(\\d+),?(\\d+)?\\s@@", Chunk },
                { "^-", DeleteLine },
                { "^\\+", AddLine },
                { "^Binary files (.+) and (.+) differ", BinaryDiff }
            };
        }

        public IEnumerable<FileDiff> Run(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                if (!ParseLine(line))
                    ParseNormalLine(line);
            }

            return files;
        }

        private void Start(string? line)
        {
            file = new FileDiff();
            files.Add(file);
            if (file.To == null && file.From == null)
            {
                string[]? array = ParseFileNames(line);
                if (array is not null)
                {
                    file.From = array[0];
                    file.To = array[1];
                }
            }
        }

        private void Restart()
        {
            if (file == null || file.Chunks.Count != 0)
                Start(null);
        }

        private void NewFile()
        {
            Restart();
            file.Type = FileChangeType.Add;
            file.From = "/dev/null";
        }

        private void DeletedFile()
        {
            Restart();
            file.Type = FileChangeType.Delete;
            file.To = "/dev/null";
        }

        private void Index(string line)
        {
            Restart();
            file.Index = line.Split(new char[1] { ' ' }).Skip(1);
        }

        private void FromFile(string line)
        {
            Restart();
            file.From = ParseFileName(line);
        }

        private void ToFile(string line)
        {
            Restart();
            file.To = ParseFileName(line);
        }

        private void BinaryDiff()
        {
            Restart();
            file.Type = FileChangeType.Modified;
        }

        private void Chunk(string line, Match match)
        {
            in_del = (oldStart = int.Parse(match.Groups[1].Value));
            oldLines = (match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0);
            in_add = (newStart = int.Parse(match.Groups[3].Value));
            newLines = (match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0);
            ChunkRangeInfo rangeInfo = new ChunkRangeInfo(new ChunkRange(oldStart, oldLines), new ChunkRange(newStart, newLines));
            current = new Chunk(line, rangeInfo);
            file.Chunks.Add(current);
        }

        private void DeleteLine(string line)
        {
            string content = DiffLineHelper.GetContent(line);
            current.Changes.Add(new LineDiff(LineChangeType.Delete, in_del++, content));
            file.Deletions++;
        }

        private void AddLine(string line)
        {
            string content = DiffLineHelper.GetContent(line);
            current.Changes.Add(new LineDiff(LineChangeType.Add, in_add++, content));
            file.Additions++;
        }

        private void ParseNormalLine(string line)
        {
            if (file != null && !string.IsNullOrEmpty(line))
            {
                string content = DiffLineHelper.GetContent(line);
                current.Changes.Add(new LineDiff((!(line == "\\ No newline at end of file")) ? in_del++ : 0, (!(line == "\\ No newline at end of file")) ? in_add++ : 0, content));
            }
        }

        private bool ParseLine(string line)
        {
            foreach (HandlerRow item in schema)
            {
                Match match = item.Expression.Match(line);
                if (match.Success)
                {
                    item.Action(line, match);
                    return true;
                }
            }

            return false;
        }

        private static string[]? ParseFileNames(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return null;

            return (from fileName in s.Split(new char[1] { ' ' }).Reverse().Take(2)
                    .Reverse()
                    select Regex.Replace(fileName, "^(a|b)\\/", "")).ToArray();
        }

        private static string ParseFileName(string s)
        {
            s = s.TrimStart('-', '+');
            s = s.Trim();
            Match match = new Regex("\\t.*|\\d{4}-\\d\\d-\\d\\d\\s\\d\\d:\\d\\d:\\d\\d(.\\d+)?\\s(\\+|-)\\d\\d\\d\\d").Match(s);
            if (match.Success)
            {
                s = s.Substring(0, match.Index).Trim();
            }

            if (!Regex.IsMatch(s, "^(a|b)\\/"))
            {
                return s;
            }

            return s.Substring(2);
        }

        internal class DiffLineHelper
        {
            public static string GetContent(string line)
            {
                return line.Substring(1);
            }
        }
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public class FileDiff
    {
        public ICollection<Chunk> Chunks { get; } = new List<Chunk>();

        public int Deletions { get; set; }

        public int Additions { get; set; }

        public string To { get; set; }

        public string From { get; set; }

        public FileChangeType Type { get; set; }

        public bool Deleted => Type == FileChangeType.Delete;

        public bool Add => Type == FileChangeType.Add;

        public IEnumerable<string> Index { get; set; }
    }
#pragma warning restore CS8618

    public class Chunk
    {
        public ICollection<LineDiff> Changes { get; }

        public string Content { get; }

        public ChunkRangeInfo RangeInfo { get; }

        public Chunk(string _content, ChunkRangeInfo _rangeInfo)
        {
            Content = _content;
            RangeInfo = _rangeInfo;
            Changes = new List<LineDiff>();
        }
    }

    public class LineDiff
    {
        public bool Add => Type == LineChangeType.Add;

        public bool Delete => Type == LineChangeType.Delete;

        public bool Normal => Type == LineChangeType.Normal;

        public string Content { get; }

        public int Index { get; }

        public int OldIndex { get; }

        public int NewIndex { get; }

        public LineChangeType Type { get; }

        public LineDiff(LineChangeType _type, int _index, string _content)
        {
            Type = _type;
            Index = _index;
            Content = _content;
        }

        public LineDiff(int _oldIndex, int _newIndex, string _content)
        {
            OldIndex = _oldIndex;
            NewIndex = _newIndex;
            Type = LineChangeType.Normal;
            Content = _content;
        }
    }

    public class ChunkRangeInfo
    {
        public ChunkRange OriginalRange { get; }

        public ChunkRange NewRange { get; }

        public ChunkRangeInfo(ChunkRange _originalRange, ChunkRange _newRange)
        {
            OriginalRange = _originalRange;
            NewRange = _newRange;
        }
    }
    public class ChunkRange
    {
        public int StartLine { get; }

        public int LineCount { get; }

        public ChunkRange(int _startLine, int _lineCount)
        {
            StartLine = _startLine;
            LineCount = _lineCount;
        }
    }

    public enum FileChangeType
    {
        Modified,
        Add,
        Delete
    }
    public enum LineChangeType
    {
        Normal,
        Add,
        Delete
    }
}
