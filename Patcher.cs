using Serilog;

namespace BeamNG.RemoteControlPatcher
{
    public class Patcher
    {
        /// <summary>
        /// Arg1 - from file, Arg2 - to file
        /// </summary>
        public event Action<string, string>? BeforeEditFile;

        private string patchesLoation;
        private string filesLocation;

        private HashSet<string> hexDumpFiles = new HashSet<string>();

        public Patcher(string _patchesLoation, string _filesLocation = "")
        {
            patchesLoation = _patchesLoation;
            filesLocation = _filesLocation;
        }

        public void PatchAll(IEnumerable<string> patchNames)
        {
            foreach (var name in patchNames)
                patch(name);
        }

        private void patch(string patchName)
        {
            string path = Path.Combine(patchesLoation, patchName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Patch '{patchName}' doesn't exist");

            Log.Information($"Applying patch '{patchName}'");
            patch(File.ReadAllText(path), patchName);
            Log.Debug($"Done");
        }

        private void patch(string patch, string patchName, Dictionary<string, string>? variables = null)
        {
            var filesToPatch = parse(patch);

            foreach (var filePatch in filesToPatch)
                patchFile(filePatch, patchName);
        }

        private void patchFile(FileDiff patch, string patchName)
        {
            FileInfo from = new FileInfo(Path.Combine(filesLocation, patch.From));
            FileInfo to = new FileInfo(Path.Combine(filesLocation, patch.To));

            if (!from.Exists)
                throw new IOException($"File '{from.FullName}' doesn't exist, it is used by patch '{patchName}'");

            BeforeEditFile?.Invoke(patch.From, patch.To);

            List<string> lines = File.ReadAllLines(from.FullName).ToList();

            foreach (var chunk in patch.Chunks)
                patchChunk(chunk, lines);

            File.WriteAllLines(to.FullName, lines);
        }

        private void patchChunk(Chunk chunk, List<string> lines)
        {
            ChunkRange range = chunk.RangeInfo.NewRange;

            int lineIndex = range.StartLine - 1;
            foreach (var change in chunk.Changes)
            {
                switch (change.Type)
                {
                    case LineChangeType.Normal:
                        lineIndex++;
                        break;
                    case LineChangeType.Add:
                        lines.Insert(lineIndex++, change.Content.Replace("\r", string.Empty).Replace("\n", string.Empty));
                        break;
                    case LineChangeType.Delete:
                        lines.RemoveAt(lineIndex);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown {nameof(LineChangeType)}: '{change.Type}'");
                }
            }
        }

        static IEnumerable<FileDiff> parse(string patch)
        {
            if (string.IsNullOrWhiteSpace(patch))
                return Enumerable.Empty<FileDiff>();

            IEnumerable<string> enumerable = splitLines(patch);
            if (!enumerable.Any())
                return Enumerable.Empty<FileDiff>();

            return new DiffParser().Run(enumerable);

            IEnumerable<string> splitLines(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return Enumerable.Empty<string>();

                string[] array = input.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (array.Length != 0)
                    return array;

                return Enumerable.Empty<string>();
            }
        }
    }
}
