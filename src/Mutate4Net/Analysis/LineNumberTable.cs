namespace Mutate4Net.Analysis;

internal sealed class LineNumberTable
{
    private readonly int[] _lineStarts;

    public LineNumberTable(string source)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n' && i + 1 < source.Length)
            {
                starts.Add(i + 1);
            }
        }

        _lineStarts = starts.ToArray();
    }

    public int LineNumber(int position)
    {
        int index = Array.BinarySearch(_lineStarts, position);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return Math.Max(0, index) + 1;
    }
}

