using System;
using TaleWorlds.Library;

namespace WatchtowerNetwork.Heatmaps;

public sealed class HeatmapAStarDistanceSolver
{
    private readonly int _width;
    private readonly int _height;
    private readonly float _gridStep;
    private readonly float _minX;
    private readonly float _minY;
    private readonly bool[] _walkable;
    private readonly float[] _gScore;
    private readonly int[] _gScoreStamp;
    private readonly int[] _closedStamp;
    private int _stamp = 1;

    public HeatmapAStarDistanceSolver(HeatmapHeader header, HeatmapCell[] cells)
    {
        _width = header.GridWidth;
        _height = header.GridHeight;
        _gridStep = header.GridStep;
        _minX = header.MinX;
        _minY = header.MinY;
        _walkable = new bool[cells.Length];
        _gScore = new float[cells.Length];
        _gScoreStamp = new int[cells.Length];
        _closedStamp = new int[cells.Length];

        for (int i = 0; i < cells.Length; i++)
        {
            _walkable[i] = cells[i].IsLand;
        }
    }

    public bool TryGetPathDistance(Vec2 startWorld, Vec2 endWorld, float maxDistance, out float distance)
    {
        distance = 0f;
        if (_walkable.Length == 0 || _gridStep <= 0f)
        {
            return false;
        }

        if (maxDistance <= 0f)
        {
            return false;
        }

        int startX = WorldToGridX(startWorld.X);
        int startY = WorldToGridY(startWorld.Y);
        int endX = WorldToGridX(endWorld.X);
        int endY = WorldToGridY(endWorld.Y);

        if (!IsInside(startX, startY) || !IsInside(endX, endY))
        {
            return false;
        }

        int start = FindNearestWalkableIndex(startX, startY);
        int goal = FindNearestWalkableIndex(endX, endY);
        if (start < 0 || goal < 0)
        {
            return false;
        }

        if (start == goal)
        {
            distance = 0f;
            return true;
        }

        IncrementStamp();
        MinHeap open = new MinHeap(Math.Max(32, _width));
        SetGScore(start, 0f);
        open.Push(new HeapNode(start, Heuristic(start, goal)));

        while (open.Count > 0)
        {
            HeapNode current = open.Pop();
            int currentIndex = current.Index;
            if (IsClosed(currentIndex))
            {
                continue;
            }

            float currentG = GetGScore(currentIndex);
            if (current.FScore > currentG + Heuristic(currentIndex, goal) + 0.001f)
            {
                continue;
            }

            if (currentG > maxDistance)
            {
                continue;
            }

            if (currentIndex == goal)
            {
                distance = currentG;
                return true;
            }

            MarkClosed(currentIndex);
            int cx = currentIndex % _width;
            int cy = currentIndex / _width;

            for (int ny = cy - 1; ny <= cy + 1; ny++)
            {
                if (ny < 0 || ny >= _height)
                {
                    continue;
                }

                for (int nx = cx - 1; nx <= cx + 1; nx++)
                {
                    if (nx < 0 || nx >= _width || (nx == cx && ny == cy))
                    {
                        continue;
                    }

                    int next = ToIndex(nx, ny);
                    if (!_walkable[next] || IsClosed(next))
                    {
                        continue;
                    }

                    float stepCost = ((nx == cx) || (ny == cy)) ? _gridStep : _gridStep * 1.4142135f;
                    float tentativeG = currentG + stepCost;
                    if (tentativeG > maxDistance)
                    {
                        continue;
                    }

                    float heuristic = Heuristic(next, goal);
                    if (tentativeG + heuristic > maxDistance)
                    {
                        continue;
                    }

                    if (tentativeG + 0.001f >= GetGScore(next))
                    {
                        continue;
                    }

                    SetGScore(next, tentativeG);
                    open.Push(new HeapNode(next, tentativeG + heuristic));
                }
            }
        }

        return false;
    }

    private int FindNearestWalkableIndex(int centerX, int centerY)
    {
        if (!IsInside(centerX, centerY))
        {
            return -1;
        }

        int center = ToIndex(centerX, centerY);
        if (_walkable[center])
        {
            return center;
        }

        const int maxRadius = 8;
        int bestIndex = -1;
        int bestDistanceSquared = int.MaxValue;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            int minX = Math.Max(0, centerX - radius);
            int maxX = Math.Min(_width - 1, centerX + radius);
            int minY = Math.Max(0, centerY - radius);
            int maxY = Math.Min(_height - 1, centerY + radius);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    bool isPerimeter = x == minX || x == maxX || y == minY || y == maxY;
                    if (!isPerimeter)
                    {
                        continue;
                    }

                    int idx = ToIndex(x, y);
                    if (!_walkable[idx])
                    {
                        continue;
                    }

                    int dx = x - centerX;
                    int dy = y - centerY;
                    int d2 = (dx * dx) + (dy * dy);
                    if (d2 < bestDistanceSquared)
                    {
                        bestDistanceSquared = d2;
                        bestIndex = idx;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                return bestIndex;
            }
        }

        return -1;
    }

    private float Heuristic(int fromIndex, int toIndex)
    {
        int fromX = fromIndex % _width;
        int fromY = fromIndex / _width;
        int toX = toIndex % _width;
        int toY = toIndex / _width;
        float dx = (fromX - toX) * _gridStep;
        float dy = (fromY - toY) * _gridStep;
        return (float)Math.Sqrt((dx * dx) + (dy * dy));
    }

    private float GetGScore(int index)
    {
        return _gScoreStamp[index] == _stamp ? _gScore[index] : float.MaxValue;
    }

    private void SetGScore(int index, float score)
    {
        _gScoreStamp[index] = _stamp;
        _gScore[index] = score;
    }

    private bool IsClosed(int index)
    {
        return _closedStamp[index] == _stamp;
    }

    private void MarkClosed(int index)
    {
        _closedStamp[index] = _stamp;
    }

    private void IncrementStamp()
    {
        if (_stamp == int.MaxValue)
        {
            Array.Clear(_gScoreStamp, 0, _gScoreStamp.Length);
            Array.Clear(_closedStamp, 0, _closedStamp.Length);
            _stamp = 1;
            return;
        }

        _stamp++;
    }

    private int WorldToGridX(float x)
    {
        return (int)Math.Round((x - _minX) / _gridStep, MidpointRounding.AwayFromZero);
    }

    private int WorldToGridY(float y)
    {
        return (int)Math.Round((y - _minY) / _gridStep, MidpointRounding.AwayFromZero);
    }

    private bool IsInside(int x, int y)
    {
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }

    private int ToIndex(int x, int y)
    {
        return (y * _width) + x;
    }

    private readonly struct HeapNode
    {
        public int Index { get; }
        public float FScore { get; }

        public HeapNode(int index, float fScore)
        {
            Index = index;
            FScore = fScore;
        }
    }

    private sealed class MinHeap
    {
        private HeapNode[] _items;
        public int Count { get; private set; }

        public MinHeap(int initialCapacity)
        {
            _items = new HeapNode[Math.Max(4, initialCapacity)];
        }

        public void Push(HeapNode node)
        {
            if (Count >= _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            int i = Count++;
            _items[i] = node;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[parent].FScore <= _items[i].FScore)
                {
                    break;
                }

                (_items[parent], _items[i]) = (_items[i], _items[parent]);
                i = parent;
            }
        }

        public HeapNode Pop()
        {
            HeapNode min = _items[0];
            int lastIndex = --Count;
            _items[0] = _items[lastIndex];
            int i = 0;
            while (true)
            {
                int left = (i * 2) + 1;
                int right = left + 1;
                if (left >= Count)
                {
                    break;
                }

                int smaller = left;
                if (right < Count && _items[right].FScore < _items[left].FScore)
                {
                    smaller = right;
                }

                if (_items[i].FScore <= _items[smaller].FScore)
                {
                    break;
                }

                (_items[i], _items[smaller]) = (_items[smaller], _items[i]);
                i = smaller;
            }

            return min;
        }
    }
}
