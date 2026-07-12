namespace OpenKO.Servers.AIServer;

/// <summary>
/// Port of <c>CPathFind</c> (Server/AIServer/PathFind.cpp) — the NPC A* search,
/// replicated with its original behavior:
/// <list type="bullet">
/// <item>start node heuristic = euclidean distance, child heuristic =
///   <c>max(x-dx, y-dy)</c> (unclamped, can go negative) — as in the C++,</item>
/// <item>step costs 10 (straight) / 11 (diagonal), PropagateDown re-parenting
///   with its quirky <c>f = parent.g + parent.h</c> update,</item>
/// <item>gives up after <c>maxtry*2</c> iterations and returns null,</item>
/// <item>open list kept sorted by f (insertion sort).</item>
/// </list>
/// The only deviation: the C++ writes past the 8-slot child array when a node
/// gains a ninth child (silent memory corruption); here the child is dropped.
/// A cell is walkable when the map value is 0 (<c>IsBlankMap</c>).
/// </summary>
public sealed class PathFinder
{
    private const int StepCross = 11;   // LEVEL_TWO_FIND_CROSS (diagonal neighbors)
    private const int StepDiagonal = 10; // LEVEL_TWO_FIND_DIAGONAL (straight neighbors)

    public sealed class PathNode
    {
        public int F;
        public int H;
        public int G;
        public int X;
        public int Y;
        public PathNode? Parent;
        public readonly PathNode?[] Child = new PathNode?[8];
        public PathNode? NextNode;
    }

    private PathNode? _open;
    private PathNode? _closed;
    private int[] _map = [];
    private int _sizeX;
    private int _sizeY;

    public void SetMap(int sizeX, int sizeY, int[] map)
    {
        _sizeX = sizeX;
        _sizeY = sizeY;
        _map = map;
    }

    public bool IsBlankMap(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _sizeX || y >= _sizeY)
            return false;

        return _map[x * _sizeY + y] == 0;
    }

    /// <summary>
    /// Returns the destination node (walk <see cref="PathNode.Parent"/> back to the
    /// start), or null when no path was found / the search was aborted.
    /// </summary>
    public PathNode? FindPath(int startX, int startY, int destX, int destY)
    {
        _open = new PathNode();
        _closed = new PathNode();

        var start = new PathNode
        {
            G = 0,
            H = (int)System.Math.Sqrt(
                (startX - destX) * (startX - destX) + (startY - destY) * (startY - destY)),
            X = startX,
            Y = startY,
        };
        start.F = start.G + start.H;

        int maxTry = System.Math.Abs(startX - destX) * _sizeX
            + System.Math.Abs(startY - destY) * _sizeY + 1;
        int count = 0;

        _open.NextNode = start;

        while (true)
        {
            if (count > maxTry * 2)
                return null; // search aborted, like the C++

            count++;

            PathNode? best = ReturnBestNode();
            if (best is null)
                return null;

            if (best.X == destX && best.Y == destY)
                return best;

            FindChildPath(best, destX, destY);
        }
    }

    /// <summary>Materializes a found path from start to destination.</summary>
    public static List<(int X, int Y)> ToPath(PathNode destination)
    {
        var path = new List<(int, int)>();
        for (PathNode? node = destination; node is not null; node = node.Parent)
            path.Add((node.X, node.Y));

        path.Reverse();
        return path;
    }

    private PathNode? ReturnBestNode()
    {
        if (_open?.NextNode is null)
            return null;

        PathNode node = _open.NextNode;
        _open.NextNode = node.NextNode;

        node.NextNode = _closed!.NextNode;
        _closed.NextNode = node;

        return node;
    }

    private void FindChildPath(PathNode node, int dx, int dy)
    {
        int x, y;

        // UpperLeft
        if (IsBlankMap(x = node.X - 1, y = node.Y - 1))
            FindChildPathSub(node, x, y, dx, dy, StepCross);

        // Upper
        if (IsBlankMap(x = node.X, y = node.Y - 1))
            FindChildPathSub(node, x, y, dx, dy, StepDiagonal);

        // UpperRight
        if (IsBlankMap(x = node.X + 1, y = node.Y - 1))
            FindChildPathSub(node, x, y, dx, dy, StepCross);

        // Right
        if (IsBlankMap(x = node.X + 1, y = node.Y))
            FindChildPathSub(node, x, y, dx, dy, StepDiagonal);

        // LowerRight
        if (IsBlankMap(x = node.X + 1, y = node.Y + 1))
            FindChildPathSub(node, x, y, dx, dy, StepCross);

        // Lower
        if (IsBlankMap(x = node.X, y = node.Y + 1))
            FindChildPathSub(node, x, y, dx, dy, StepDiagonal);

        // LowerLeft
        if (IsBlankMap(x = node.X - 1, y = node.Y + 1))
            FindChildPathSub(node, x, y, dx, dy, StepCross);

        // Left
        if (IsBlankMap(x = node.X - 1, y = node.Y))
            FindChildPathSub(node, x, y, dx, dy, StepDiagonal);
    }

    private void FindChildPathSub(PathNode node, int x, int y, int dx, int dy, int cost)
    {
        int g = node.G + cost;

        PathNode? oldNode;
        if ((oldNode = CheckOpen(x, y)) is not null)
        {
            AddChild(node, oldNode);

            if (g < oldNode.G)
            {
                oldNode.Parent = node;
                oldNode.G = g;
                oldNode.F = g + oldNode.H;
            }
        }
        else if ((oldNode = CheckClosed(x, y)) is not null)
        {
            AddChild(node, oldNode);

            if (g < oldNode.G)
            {
                oldNode.Parent = node;
                oldNode.G = g;
                oldNode.F = g + oldNode.H;
                PropagateDown(oldNode);
            }
        }
        else
        {
            var newNode = new PathNode
            {
                Parent = node,
                G = g,
                // NOTE: the C++ switched the child heuristic to max(x-dx, y-dy)
                // (euclidean variant commented out) — unclamped, kept verbatim.
                H = System.Math.Max(x - dx, y - dy),
                X = x,
                Y = y,
            };
            newNode.F = g + newNode.H;
            Insert(newNode);
            AddChild(node, newNode);
        }
    }

    private static void AddChild(PathNode parent, PathNode child)
    {
        for (int c = 0; c < 8; c++)
        {
            if (parent.Child[c] is null)
            {
                parent.Child[c] = child;
                return;
            }
        }

        // C++ would write out of bounds here; we drop the ninth child instead.
    }

    private PathNode? CheckOpen(int x, int y)
    {
        for (PathNode? node = _open?.NextNode; node is not null; node = node.NextNode)
        {
            if (node.X == x && node.Y == y)
                return node;
        }

        return null;
    }

    private PathNode? CheckClosed(int x, int y)
    {
        for (PathNode? node = _closed?.NextNode; node is not null; node = node.NextNode)
        {
            if (node.X == x && node.Y == y)
                return node;
        }

        return null;
    }

    private void Insert(PathNode node)
    {
        if (_open!.NextNode is null)
        {
            _open.NextNode = node;
            return;
        }

        int f = node.F;
        PathNode prev = _open;
        PathNode? current = _open.NextNode;

        while (current is not null && current.F < f)
        {
            prev = current;
            current = current.NextNode;
        }

        node.NextNode = current;
        prev.NextNode = node;
    }

    private void PropagateDown(PathNode oldNode)
    {
        var stack = new Stack<PathNode>();

        int g = oldNode.G;
        for (int c = 0; c < 8; c++)
        {
            PathNode? child = oldNode.Child[c];
            if (child is null)
                break;

            if (g + 1 < child.G)
            {
                child.G = g + 1;
                child.F = child.G + child.H;
                child.Parent = oldNode;
                stack.Push(child);
            }
        }

        while (stack.Count > 0)
        {
            PathNode parent = stack.Pop();
            for (int c = 0; c < 8; c++)
            {
                PathNode? child = parent.Child[c];
                if (child is null)
                    break;

                if (parent.G + 1 < child.G)
                {
                    child.G = parent.G + 1;
                    // Verbatim C++ quirk: f is updated from the PARENT's g+h here.
                    child.F = parent.G + parent.H;
                    child.Parent = parent;
                    stack.Push(child);
                }
            }
        }
    }
}
