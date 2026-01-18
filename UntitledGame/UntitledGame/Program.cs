using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace UntitledGame
{
    internal class Program
    {
        static Player player;
        static string[] mapLines;
        static Orb orb;
        static Random rng;
        static List<Follower> followers = new List<Follower>();
        static int score = 0;
        static long seedValue;
        static int currentWindowWidth;
        static int currentWindowHeight;
        static int mapWidth;
        static int mapHeight;
        static double RareChance = 0.05;
        static double NormalGoldenSplit = 0.5;
        static double HardGoldenChance = 0.25;
        // White enemy chances
        static double WhiteChance = 0.05;
        static double HardWhiteChance = 0.25;
        // Stealth enemy chances
        static double StealthChance = 0.10;
        static double HardStealthChance = 0.25;
        static bool CustomMode = false;
        static double CustomMagentaSpawnRate = RareChance;
        static double CustomMagentaToGoldenRate = NormalGoldenSplit;
        static double CustomOrbSpawnRate = RareChance;
        static double CustomWhiteSpawnRate = WhiteChance;
        static double CustomStealthSpawnRate = StealthChance;
        static bool HardMode = false;
        public static int GoldenSlowRadius = 10;

        // If a white enemy has spawned since the last orb-eaten, guarantee the next single spawn (the one after orb) to be golden
        // (50% chance to spawn white instead). This overrides custom-mode chances for that single spawn.
        static bool WhiteSpawnedWaitingForOrb = false;

        // Time slow + stamina
        static bool TimeSlowActive = false;
        static double StaminaMax = 20.0;
        static double Stamina = 20.0;
        // units per second
        static double StaminaConsumePerSecond = 6.0;
        static double StaminaRecoverPerSecond = 3.0;
        static int StaminaPerOrbNormal = 2;
        static int StaminaPerOrbRare = 4;
        static int lastStaminaTick = Environment.TickCount;

        static void Main(string[] args)
        {
            Console.CursorVisible = false;
            Console.Write("Enter seed (leave empty for random): ");
            string seedInput = Console.ReadLine();

            if (!string.IsNullOrEmpty(seedInput))
            {
                seedValue = ParseSeedArg(seedInput);
            }
            else
            {
                seedValue = DateTime.UtcNow.Ticks ^ Environment.MachineName.GetHashCode();
            }

            rng = new Random(ToIntSeed(seedValue));

            Console.Write("Select difficulty: (N)ormal / (H)ard / (C)ustom: ");
            var diffKey = Console.ReadKey(true).Key;
            if (diffKey == ConsoleKey.C)
            {
                Console.WriteLine(" Custom mode selected");
                ConfigureCustomChances();
                CustomMode = true;
                HardMode = false;
                Console.WriteLine("Starting game with custom settings...");
            }
            else
            {
                HardMode = (diffKey == ConsoleKey.H);
                Console.WriteLine(HardMode ? " Hard mode selected" : " Normal mode selected");
            }

            currentWindowWidth = Console.WindowWidth;
            currentWindowHeight = Console.WindowHeight;
            mapWidth = Math.Max(40, Math.Min(120, currentWindowWidth));
            mapHeight = Math.Max(20, Math.Min(40, Math.Max(1, currentWindowHeight - 1)));


            RegenerateMapAndEntities();

            // initialize stamina tick
            lastStaminaTick = Environment.TickCount;

            DrawSeed();

            orb.Draw();
            foreach (var f in followers) f.Draw();
            player.Draw();
            DrawScore();
            DrawTimeSlowIndicatorAndStamina();
            DrawSeed();

            while (true)
            {
                if (Console.WindowWidth != currentWindowWidth || Console.WindowHeight != currentWindowHeight)
                {
                    currentWindowWidth = Console.WindowWidth;
                    currentWindowHeight = Console.WindowHeight;
                    mapWidth = Math.Max(40, Math.Min(120, currentWindowWidth));
                    mapHeight = Math.Max(20, Math.Min(40, Math.Max(1, currentWindowHeight - 1)));
                    RegenerateMapAndEntities();
                    DrawScore();
                    DrawTimeSlowIndicatorAndStamina();
                    DrawSeed();
                }

                Update();

                // stamina/time-slow handling (tick-based)
                int nowTick = Environment.TickCount;
                int deltaMs = nowTick - lastStaminaTick;
                if (deltaMs > 0)
                {
                    if (TimeSlowActive)
                    {
                        // consume stamina
                        double consume = (StaminaConsumePerSecond * deltaMs) / 1000.0;
                        Stamina -= consume;
                        if (Stamina <= 0.0)
                        {
                            Stamina = 0.0;
                            TimeSlowActive = false;
                        }
                    }
                    else
                    {
                        // recover stamina
                        double recover = (StaminaRecoverPerSecond * deltaMs) / 1000.0;
                        Stamina = Math.Min(StaminaMax, Stamina + recover);
                    }
                    lastStaminaTick = nowTick;
                }

                if (player.X == orb.X && player.Y == orb.Y)
                {
                    if (orb.IsRare)
                    {
                        score += 4;
                        for (int i = 0; i < 4; i++) player.IncreaseMoveRate();
                    }
                    else
                    {
                        score++;
                        player.IncreaseMoveRate();
                    }

                    // increase stamina on orb pickup
                    if (orb.IsRare)
                        Stamina = Math.Min(StaminaMax, Stamina + StaminaPerOrbRare);
                    else
                        Stamina = Math.Min(StaminaMax, Stamina + StaminaPerOrbNormal);

                    // notify all followers that an orb was eaten so their per-instance counter increases
                    foreach (var ff in followers)
                    {
                        ff.OnOrbEaten();
                    }

                    // remove followers that reached their death threshold (random per-enemy between 5 and 50)
                    int removedByOrbDeath = followers.RemoveAll(ff => ff.IsDead);

                    orb.Respawn();

                    if (followers.RemoveAll(ff => ff.IsGolden) > 0)
                    {
                    }

                    // kill the last spawned white follower (white survives until an orb is eaten)
                    int lastWhiteIndex = followers.FindLastIndex(ff => ff.IsWhite);
                    if (lastWhiteIndex >= 0)
                    {
                        followers.RemoveAt(lastWhiteIndex);
                    }

                    // When orb eaten, spawn the single follower that is either forced by WhiteSpawnedWaitingForOrb
                    SpawnFollower();
                }

                var occupied = new HashSet<(int x, int y)>(followers.Select(ff => (ff.X, ff.Y)));
                for (int i = 0; i < followers.Count; i++)
                {
                    var f = followers[i];
                    f.TryStepTowards(player.X, player.Y, occupied);
                    if (f.X == player.X && f.Y == player.Y)
                    {
                        GameOver();
                        return;
                    }
                }

                orb.Draw();
                foreach (var f in followers) f.Draw();
                player.Draw();
                DrawScore();
                DrawTimeSlowIndicatorAndStamina();
                DrawSeed();

                Thread.Sleep(1);
            }
        }

        static void ConfigureCustomChances()
        {
            Console.WriteLine();
            Console.WriteLine("Custom chance configuration. Leave blank to keep default value shown in parentheses.");

            Console.Write($"Magenta spawn rate (default 5%): ");
            var input = Console.ReadLine();
            CustomMagentaSpawnRate = ParseChance(input, RareChance);

            Console.Write($"Magenta to Golden transform rate (default 50%): ");
            input = Console.ReadLine();
            CustomMagentaToGoldenRate = ParseChance(input, NormalGoldenSplit);

            Console.Write($"Orb spawn rate (default 5%): ");
            input = Console.ReadLine();
            CustomOrbSpawnRate = ParseChance(input, RareChance);

            Console.Write($"White spawn rate (default 5%): ");
            input = Console.ReadLine();
            CustomWhiteSpawnRate = ParseChance(input, WhiteChance);

            Console.Write($"Stealth spawn rate (default 10%): ");
            input = Console.ReadLine();
            CustomStealthSpawnRate = ParseChance(input, StealthChance);

            Console.WriteLine("Custom settings saved.");
            Console.WriteLine();
        }

        static double ParseChance(string s, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(s)) return defaultValue;
            s = s.Trim();
            bool isPercent = false;
            if (s.EndsWith("%"))
            {
                isPercent = true;
                s = s.Substring(0, s.Length - 1).Trim();
            }

            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return defaultValue;
            }

            if (isPercent)
                v = v / 100.0;

            if (!isPercent && v > 1.0)
                v = v / 100.0;

            if (v < 0) v = 0;
            if (v > 1) v = 1;
            return v;
        }

        static void RegenerateMapAndEntities()
        {
            string map = GenerateMap(mapWidth, mapHeight, rng);
            Console.Clear();
            Console.Write(map);
            mapLines = map.Split(new[] { '\n' }, StringSplitOptions.None);
            var start = FindFirstWalkable();
            if (player == null)
                player = new Player(start.x, start.y, '@');
            else
                player = new Player(start.x, start.y, player.Glyph);

            if (orb == null)
                orb = new Orb('o');
            orb.Respawn();
            int wanted = Math.Max(0, followers.Count);
            SpawnFollowers(wanted);
        }

        static void SpawnFollowers(int count)
        {
            followers.Clear();
            for (int i = 0; i < count; i++)
            {
                var f = new Follower('X');

                if (CustomMode)
                {
                    // custom stealth first, then white/magenta
                    if (rng.NextDouble() < CustomStealthSpawnRate)
                    {
                        f.MakeStealth();
                    }
                    else if (rng.NextDouble() < CustomWhiteSpawnRate)
                    {
                        f.MakeWhite();
                    }
                    else if (rng.NextDouble() < CustomMagentaSpawnRate)
                    {
                        if (rng.NextDouble() < CustomMagentaToGoldenRate)
                            f.MakeGolden();
                        else
                            f.MakeRare();
                    }
                }
                else if (HardMode)
                {
                    // in hard mode, choose stealth/white/golden/rare with defined chances
                    double r = rng.NextDouble();
                    if (r < HardStealthChance)
                    {
                        f.MakeStealth();
                    }
                    else if (r < HardStealthChance + HardWhiteChance)
                    {
                        f.MakeWhite();
                    }
                    else if (r < HardStealthChance + HardWhiteChance + HardGoldenChance)
                    {
                        f.MakeGolden();
                    }
                    else
                    {
                        f.MakeRare();
                    }
                }
                else
                {
                    // normal mode: stealth (10%), white (5%), rare/golden (5%)
                    double r = rng.NextDouble();
                    if (r < StealthChance)
                    {
                        f.MakeStealth();
                    }
                    else if (r < StealthChance + WhiteChance)
                    {
                        f.MakeWhite();
                    }
                    else if (r < StealthChance + WhiteChance + RareChance)
                    {
                        if (rng.Next(2) == 0)
                            f.MakeRare();
                        else
                            f.MakeGolden();
                    }
                }

                for (int attempt = 0; attempt < 1000; attempt++)
                {
                    int y = rng.Next(0, mapLines.Length);
                    var line = mapLines[y].Replace("\r", "");
                    if (line.Length == 0) continue;
                    int x = rng.Next(0, line.Length);
                    if (!IsWalkable(x, y)) continue;
                    int dist = Math.Abs(x - player.X) + Math.Abs(y - player.Y);
                    if (dist < 12) continue;
                    if (followers.Any(ff => ff.X == x && ff.Y == y)) continue;
                    f.SetPosition(x, y);
                    break;
                }

                if (f.X == -1 || f.Y == -1)
                {
                    f.Respawn();
                    if (followers.Any(ff => ff.X == f.X && ff.Y == f.Y))
                    {
                        for (int attempt = 0; attempt < 2000; attempt++)
                        {
                            int y = rng.Next(0, mapLines.Length);
                            var line = mapLines[y].Replace("\r", "");
                            if (line.Length == 0) continue;
                            int x = rng.Next(0, line.Length);
                            if (!IsWalkable(x, y)) continue;
                            if (followers.Any(ff => ff.X == x && ff.Y == y)) continue;
                            f.SetPosition(x, y);
                            break;
                        }
                    }
                }
                followers.Add(f);
            }
        }

        static void SpawnFollower()
        {
            var f = new Follower('X');

            // If a white enemy previously spawned and hasn't been consumed by an orb-eat yet,
            // guarantee that the next spawn (this one) will be golden, but give 50% chance to be white instead.
            // This overrides any custom-mode or normal-mode probabilities for this single spawn.
            if (WhiteSpawnedWaitingForOrb)
            {
                bool spawnWhiteInstead = rng.NextDouble() < 0.5;
                if (spawnWhiteInstead)
                    f.MakeWhite();
                else
                    f.MakeGolden();

                WhiteSpawnedWaitingForOrb = false;
            }
            else if (CustomMode)
            {
                if (rng.NextDouble() < CustomStealthSpawnRate)
                {
                    f.MakeStealth();
                }
                else if (rng.NextDouble() < CustomWhiteSpawnRate)
                {
                    f.MakeWhite();
                }
                else if (rng.NextDouble() < CustomMagentaSpawnRate)
                {
                    if (rng.NextDouble() < CustomMagentaToGoldenRate)
                        f.MakeGolden();
                    else
                        f.MakeRare();
                }
            }
            else if (HardMode)
            {
                double r = rng.NextDouble();
                if (r < HardStealthChance)
                {
                    f.MakeStealth();
                }
                else if (r < HardStealthChance + HardWhiteChance)
                {
                    f.MakeWhite();
                }
                else if (r < HardStealthChance + HardWhiteChance + HardGoldenChance)
                {
                    f.MakeGolden();
                }
                else
                {
                    f.MakeRare();
                }
            }
            else
            {
                double r = rng.NextDouble();
                if (r < StealthChance)
                {
                    f.MakeStealth();
                }
                else if (r < StealthChance + WhiteChance)
                {
                    f.MakeWhite();
                }
                else if (r < StealthChance + WhiteChance + RareChance)
                {
                    if (rng.Next(2) == 0)
                        f.MakeRare();
                    else
                        f.MakeGolden();
                }
            }

            for (int attempt = 0; attempt < 1000; attempt++)
            {
                int y = rng.Next(0, mapLines.Length);
                var line = mapLines[y].Replace("\r", "");
                if (line.Length == 0) continue;
                int x = rng.Next(0, line.Length);
                if (!IsWalkable(x, y)) continue;
                int dist = Math.Abs(x - player.X) + Math.Abs(y - player.Y);
                if (dist < 8) continue;
                if (followers.Any(ff => ff.X == x && ff.Y == y)) continue;
                f.SetPosition(x, y);
                break;
            }
            if (f.X == -1 || f.Y == -1) f.Respawn();
            if (followers.Any(ff => ff.X == f.X && ff.Y == f.Y))
            {
                for (int attempt = 0; attempt < 200; attempt++)
                {
                    int y = rng.Next(0, mapLines.Length);
                    var line = mapLines[y].Replace("\r", "");
                    if (line.Length == 0) continue;
                    int x = rng.Next(0, line.Length);
                    if (!IsWalkable(x, y)) continue;
                    if (followers.Any(ff => ff.X == x && ff.Y == x)) continue;
                    if (Math.Abs(x - player.X) + Math.Abs(y - player.Y) < 6) continue;
                    f.SetPosition(x, y);
                    break;
                }
            }
            followers.Add(f);
        }

        static void GameOver()
        {
            try
            {
                int curX = Console.CursorLeft, curY = Console.CursorTop;
                int midX = Math.Max(0, Console.WindowWidth / 2 - 10);
                int midY = Math.Max(0, Console.WindowHeight / 2 - 1);
                Console.SetCursorPosition(midX, midY);
                var oldFg = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("GAME OVER");
                Console.ForegroundColor = oldFg;
                Console.SetCursorPosition(midX, midY + 1);
                Console.Write($"Score: {score}");
                Console.SetCursorPosition(midX, midY + 3);
                Console.Write("Press Enter to exit...");
                while (Console.ReadKey(true).Key != ConsoleKey.Enter) { }
            }
            catch { }
            Environment.Exit(0);
        }

        static void DrawScore()
        {
            try
            {
                int curX = Console.CursorLeft;
                int curY = Console.CursorTop;
                int y = Math.Max(0, Console.WindowHeight - 1);

                // Reserve left area for time-slow indicator + stamina bar
                int leftReserve = 2 + (int)StaminaMax + 1;
                leftReserve = Math.Min(leftReserve, Console.WindowWidth - 1);

                string text = $"Score: {score}";
                int seedTextLen = ("Seed: " + seedValue).Length;
                int rightLimit = Math.Max(leftReserve + text.Length, Math.Max(leftReserve, Console.WindowWidth - seedTextLen));
                int pad = Math.Max(0, rightLimit - leftReserve - text.Length);

                Console.SetCursorPosition(Math.Max(0, leftReserve), y);
                Console.Write(text + new string(' ', pad));
                Console.SetCursorPosition(curX, curY);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        static void DrawSeed()
        {
            try
            {
                int curX = Console.CursorLeft;
                int curY = Console.CursorTop;
                string text = $"Seed: {seedValue}";
                int y = Math.Max(0, Console.WindowHeight - 1);
                int x = Math.Max(0, Console.WindowWidth - text.Length);
                Console.SetCursorPosition(x, y);
                Console.Write(text);
                Console.SetCursorPosition(curX, curY);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        // Draw the bottom-left time slow indicator and stamina bar.
        static void DrawTimeSlowIndicatorAndStamina()
        {
            try
            {
                int y = Math.Max(0, Console.WindowHeight - 1);
                int curX = Console.CursorLeft;
                int curY = Console.CursorTop;

                // Indicator: uppercase 'T' at bottom-left (x=0)
                Console.SetCursorPosition(0, y);
                var oldFg = Console.ForegroundColor;
                var oldBg = Console.BackgroundColor;
                if (TimeSlowActive)
                {
                    // inverted: white background, black text
                    Console.BackgroundColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Black;
                }
                else
                {
                    // normal: default colors (keep existing bg)
                    Console.BackgroundColor = oldBg;
                    Console.ForegroundColor = oldFg;
                }
                Console.Write('T');

                // restore colors for drawing bar backgrounds below as needed
                Console.BackgroundColor = oldBg;
                Console.ForegroundColor = oldFg;

                // Stamina bar starts at x=2
                int startX = 2;
                int barWidth = (int)StaminaMax;
                for (int i = 0; i < barWidth; i++)
                {
                    Console.SetCursorPosition(startX + i, y);
                    // used portion grows from left as stamina is consumed
                    int usedCount = barWidth - (int)Math.Ceiling(Stamina);
                    if (usedCount < 0) usedCount = 0;
                    if (usedCount > barWidth) usedCount = barWidth;

                    if (i < usedCount)
                    {
                        // used stamina: white underscores with black background (visible)
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write('_');
                    }
                    else
                    {
                        // remaining stamina: white underscores on white background (invisible/blank)
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write('_');
                    }
                }

                // restore original colors and cursor
                Console.ForegroundColor = oldFg;
                Console.BackgroundColor = oldBg;
                Console.SetCursorPosition(curX, curY);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        static List<(int x, int y)> FindPathAStar(int sx, int sy, int gx, int gy, HashSet<(int x, int y)> blocked = null)
        {
            blocked = blocked ?? new HashSet<(int x, int y)>();
            var start = (x: sx, y: sy);
            var goal = (x: gx, y: gy);
            if (!IsWalkable(goal.x, goal.y)) return null;
            var open = new HashSet<(int x, int y)>();
            var closed = new HashSet<(int x, int y)>();
            var gScore = new Dictionary<(int x, int y), int>();
            var fScore = new Dictionary<(int x, int y), int>();
            var cameFrom = new Dictionary<(int x, int y), (int x, int y)>();
            Func<(int x, int y), int> heuristic = n => Math.Abs(n.x - goal.x) + Math.Abs(n.y - goal.y);
            open.Add(start);
            gScore[start] = 0;
            fScore[start] = heuristic(start);
            while (open.Count > 0)
            {
                (int x, int y) current = open.OrderBy(n => fScore.ContainsKey(n) ? fScore[n] : int.MaxValue).First();
                if (current.x == goal.x && current.y == goal.y)
                {
                    var path = new List<(int x, int y)>();
                    var cur = current;
                    path.Add(cur);
                    while (cameFrom.ContainsKey(cur))
                    {
                        cur = cameFrom[cur];
                        path.Add(cur);
                    }
                    path.Reverse();
                    return path;
                }
                open.Remove(current);
                closed.Add(current);
                var neighbors = new (int dx, int dy)[]
                {
                    (1,0), (-1,0), (0,1), (0,-1)
                };
                foreach (var n in neighbors)
                {
                    var neighbor = (x: current.x + n.dx, y: current.y + n.dy);
                    if (neighbor.x < 0 || neighbor.y < 0) continue;
                    if (!IsWalkable(neighbor.x, neighbor.y)) continue;
                    if (blocked.Contains(neighbor)) continue;
                    if (closed.Contains(neighbor)) continue;
                    int tentativeG = gScore[current] + 1;
                    if (!open.Contains(neighbor))
                    {
                        open.Add(neighbor);
                    }
                    else if (gScore.ContainsKey(neighbor) && tentativeG >= gScore[neighbor])
                    {
                        continue;
                    }
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + heuristic(neighbor);
                }
            }
            return null;
        }

        static (int x, int y) FindFirstWalkable()
        {
            if (mapLines == null) return (1, 1);
            for (int y = 0; y < mapLines.Length; y++)
            {
                var line = mapLines[y].Replace("\r", "");
                for (int x = 0; x < line.Length; x++)
                {
                    if (IsWalkable(x, y))
                        return (x, y);
                }
            }
            return (1, 1);
        }

        static long ParseSeedArg(string arg)
        {
            long value;
            if (long.TryParse(arg, out value))
                return value;
            return StringToLongSeed(arg);
        }

        static long StringToLongSeed(string s)
        {
            const ulong FNV_offset_basis = 14695981039346656037UL;
            const ulong FNV_prime = 1099511628211UL;
            ulong hash = FNV_offset_basis;
            foreach (var c in s)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= FNV_prime;
                hash ^= (byte)(c >> 8);
                hash *= FNV_prime;
            }
            unchecked
            {
                return (long)hash;
            }
        }

        static int ToIntSeed(long seed)
        {
            unchecked
            {
                return (int)(seed ^ (seed >> 32));
            }
        }

        static string GenerateMap(int width, int height, Random rng)
        {
            var grid = new char[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid[y, x] = '#';
            int roomAttempts = 60;
            int minRoomW = 6;
            int minRoomH = 4;
            int maxRoomW = Math.Max(8, width / 4);
            int maxRoomH = Math.Max(6, height / 4);
            var rooms = new List<(int x, int y, int w, int h)>();
            bool Overlaps((int x, int y, int w, int h) a, (int x, int y, int w, int h) b)
            {
                return a.x < b.x + b.w + 1 && a.x + a.w + 1 > b.x && a.y < b.y + b.h + 1 && a.y + a.h + 1 > b.y;
            }
            for (int i = 0; i < roomAttempts; i++)
            {
                int rw = rng.Next(minRoomW, maxRoomW + 1);
                int rh = rng.Next(minRoomH, maxRoomH + 1);
                int rx = rng.Next(1, Math.Max(2, width - rw - 1));
                int ry = rng.Next(1, Math.Max(2, height - rh - 1));
                var candidate = (rx, ry, rw, rh);
                bool anyOverlap = false;
                foreach (var r in rooms)
                {
                    if (Overlaps(candidate, r))
                    {
                        anyOverlap = true;
                        break;
                    }
                }
                if (!anyOverlap)
                {
                    rooms.Add(candidate);
                }
            }
            var centers = new List<(int x, int y)>();
            foreach (var r in rooms)
            {
                for (int yy = r.y; yy < r.y + r.h; yy++)
                    for (int xx = r.x; xx < r.x + r.w; xx++)
                        if (yy > 0 && yy < height - 1 && xx > 0 && xx < width - 1)
                            grid[yy, xx] = ' ';
                centers.Add((r.x + r.w / 2, r.y + r.h / 2));
            }
            if (rooms.Count == 0)
            {
                int cxw = Math.Min(width - 4, 20);
                int cyh = Math.Min(height - 4, 10);
                int cx = (width - cxw) / 2;
                int cy = (height - cyh) / 2;
                for (int yy = cy; yy < cy + cyh; yy++)
                    for (int xx = cx; xx < cx + cxw; xx++)
                        grid[yy, xx] = ' ';
                centers.Add((cx + cxw / 2, cy + cyh / 2));
            }
            var connected = new HashSet<int>();
            if (centers.Count > 0) connected.Add(0);
            var remaining = centers.Count > 1 ? Enumerable.Range(1, centers.Count - 1).ToList() : new List<int>();
            while (remaining.Count > 0)
            {
                int bestRem = -1, bestConn = -1; int bestDist = int.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ri = remaining[i];
                    for (int c = 0; c < centers.Count; c++)
                    {
                        if (!connected.Contains(c)) continue;
                        int dx = centers[ri].x - centers[c].x;
                        int dy = centers[ri].y - centers[c].y;
                        int dist = dx * dx + dy * dy;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestRem = ri;
                            bestConn = c;
                        }
                    }
                }
                var a = centers[bestConn];
                var b = centers[bestRem];
                int x = a.x, y = a.y;
                if (rng.Next(2) == 0)
                {
                    while (x != b.x)
                    {
                        if (x > 0 && x < width - 1 && y > 0 && y < height - 1) grid[y, x] = '.';
                        x += Math.Sign(b.x - x);
                    }
                    while (y != b.y)
                    {
                        if (x > 0 && x < width - 1 && y > 0 && y < height - 1) grid[y, x] = '.';
                        y += Math.Sign(b.y - y);
                    }
                }
                else
                {
                    while (y != b.y)
                    {
                        if (x > 0 && x < width - 1 && y > 0 && y < height - 1) grid[y, x] = '.';
                        y += Math.Sign(b.y - y);
                    }
                    while (x != b.x)
                    {
                        if (x > 0 && x < width - 1 && y > 0 && y < height - 1) grid[y, x] = '.';
                        x += Math.Sign(b.x - x);
                    }
                }
                grid[b.y, b.x] = '.';
                connected.Add(bestRem);
                remaining.Remove(bestRem);
            }
            int pillarAttempts = Math.Max(5, rooms.Count);
            for (int i = 0; i < pillarAttempts; i++)
            {
                if (centers.Count == 0) break;
                int rIndex = rng.Next(centers.Count);
                var center = centers[rIndex];
                int px = center.x + rng.Next(-2, 3);
                int py = center.y + rng.Next(-2, 3);
                if (px > 0 && px < width - 1 && py > 0 && py < height - 1 && grid[py, px] == ' ')
                    grid[py, px] = '#';
            }
            var rows = new List<string>(height);
            var sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                sb.Clear();
                for (int x = 0; x < width; x++) sb.Append(grid[y, x]);
                rows.Add(sb.ToString());
            }
            return string.Join("\n", rows);
        }

        static void Draw(int x, int y, string output)
        {
            try
            {
                int currentX = Console.CursorLeft;
                int currentY = Console.CursorTop;
                if (y >= 0 && y < Console.WindowHeight && x >= 0 && x < Console.WindowWidth)
                    Console.SetCursorPosition(x, y);
                Console.Write(output);
                if (currentX >= 0 && currentX < Console.WindowWidth && currentY >= 0 && currentY < Console.WindowHeight)
                    Console.SetCursorPosition(currentX, currentY);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        public static bool IsWall(int x, int y)
        {
            if (mapLines == null) return false;
            if (y < 0 || y >= mapLines.Length) return false;
            var line = mapLines[y].Replace("\r", "");
            if (x < 0 || x >= line.Length) return false;
            return line[x] == '#';
        }

        public static bool IsWalkable(int x, int y)
        {
            if (mapLines == null) return false;
            if (y < 0 || y >= mapLines.Length) return false;
            var line = mapLines[y].Replace("\r", "");
            if (x < 0 || x >= line.Length) return false;
            return line[x] != '#';
        }

        static void Update()
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.A:
                        player.TryMove(-1, 0);
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.D:
                        player.TryMove(1, 0);
                        break;
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.W:
                        player.TryMove(0, -1);
                        break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.S:
                        player.TryMove(0, 1);
                        break;
                    case ConsoleKey.T:
                        // toggle time slow; only allow if stamina available
                        if (!TimeSlowActive && Stamina <= 0.0)
                        {
                            // ignore toggle if no stamina
                        }
                        else
                        {
                            TimeSlowActive = !TimeSlowActive;
                            // reset tick so changes apply immediately
                            lastStaminaTick = Environment.TickCount;
                        }
                        break;
                    case ConsoleKey.Escape:
                        Environment.Exit(0);
                        break;
                }
            }
        }

        private class Player
        {
            public int X { get; private set; }
            public int Y { get; private set; }
            public char Glyph { get; private set; }

            private int prevX;
            private int prevY;
            private int moveDelayMs = 200;
            private int lastMoveTick;
            private const int MinMoveDelayMs = 1;
            private const int MoveDelayDecrease = 20;

            public Player(int x, int y, char glyph)
            {
                X = x;
                Y = y;
                Glyph = glyph;
                prevX = X;
                prevY = Y;
                lastMoveTick = Environment.TickCount - moveDelayMs;
            }

            public int GetMoveDelayMs()
            {
                return moveDelayMs;
            }

            public void TryMove(int dx, int dy)
            {
                int now = Environment.TickCount;
                int elapsed = now - lastMoveTick;
                if (elapsed < moveDelayMs)
                {
                    return;
                }

                Move(dx, dy);
                lastMoveTick = Environment.TickCount;
            }

            public void IncreaseMoveRate()
            {
                moveDelayMs = Math.Max(MinMoveDelayMs, moveDelayMs - MoveDelayDecrease);
            }

            private void Move(int dx, int dy)
            {
                int targetX = X + dx;
                int targetY = Y + dy;
                if (Program.IsWall(targetX, targetY))
                {
                    return;
                }

                prevX = X;
                prevY = Y;

                X = targetX;
                Y = targetY;

                if (X < 0) X = 0;
                if (Y < 0) Y = 0;
                if (X >= Console.BufferWidth) X = Console.BufferWidth - 1;
                if (Y >= Console.BufferHeight) Y = Console.BufferHeight - 1;
            }

            public void Draw()
            {
                try
                {
                    Console.SetCursorPosition(prevX, prevY);
                    char underlying = ' ';
                    if (mapLines != null && prevY >= 0 && prevY < mapLines.Length)
                    {
                        var line = mapLines[prevY].Replace("\r", "");
                        if (prevX >= 0 && prevX < line.Length)
                            underlying = line[prevX];
                    }
                    Console.Write(underlying);

                    Console.SetCursorPosition(X, Y);
                    var old = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(Glyph);
                    Console.ForegroundColor = old;
                }
                catch (ArgumentOutOfRangeException)
                {
                    if (X >= Console.BufferWidth) X = Console.BufferWidth - 1;
                    if (Y >= Console.BufferHeight) Y = Console.BufferHeight - 1;
                }

                prevX = X;
                prevY = Y;
            }
        }

        private class Orb
        {
            public int X { get; private set; }
            public int Y { get; private set; }
            public char Glyph { get; private set; }

            private int prevX;
            private int prevY;

            public bool IsRare { get; private set; }

            public Orb(char glyph)
            {
                Glyph = glyph;
                X = 0;
                Y = 0;
                prevX = -1;
                prevY = -1;
                IsRare = false;
            }

            public void Respawn()
            {
                if (Program.CustomMode)
                    IsRare = Program.HardMode || Program.rng.NextDouble() < CustomOrbSpawnRate;
                else
                    IsRare = Program.HardMode || Program.rng.NextDouble() < RareChance;

                for (int attempt = 0; attempt < 10000; attempt++)
                {
                    int y = Program.rng.Next(0, mapLines.Length);
                    var line = mapLines[y].Replace("\r", "");
                    if (line.Length == 0) continue;
                    int x = Program.rng.Next(0, line.Length);

                    if (Program.IsWalkable(x, y) && !(Program.player != null && Program.player.X == x && Program.player.Y == y))
                    {
                        prevX = X;
                        prevY = Y;
                        X = x;
                        Y = y;
                        return;
                    }
                }

                if (player != null)
                {
                    prevX = X;
                    prevY = Y;
                    X = player.X;
                    Y = player.Y;
                }
            }

            public void Draw()
            {
                try
                {
                    if (prevX >= 0 && (prevX != Program.player.X || prevY != Program.player.Y))
                    {
                        Console.SetCursorPosition(prevX, prevY);
                        char underlying = ' ';
                        if (mapLines != null && prevY >= 0 && prevY < mapLines.Length)
                        {
                            var line = mapLines[prevY].Replace("\r", "");
                            if (prevX >= 0 && prevX < line.Length)
                                underlying = line[prevX];
                        }
                        Console.Write(underlying);
                    }

                    Console.SetCursorPosition(X, Y);
                    var old = Console.ForegroundColor;
                    Console.ForegroundColor = IsRare ? ConsoleColor.Magenta : ConsoleColor.Yellow;
                    Console.Write(Glyph);
                    Console.ForegroundColor = old;
                }
                catch (ArgumentOutOfRangeException)
                {
                }

                prevX = X;
                prevY = Y;
            }
        }

        private class Follower
        {
            public int X { get; private set; } = -1;
            public int Y { get; private set; } = -1;
            public char Glyph { get; private set; }

            private int prevX = -1;
            private int prevY = -1;
            private int moveDelayMs;
            private int lastMoveTick;

            public bool IsRare { get; private set; }
            public bool IsGolden { get; private set; }
            public bool IsWhite { get; private set; }
            public bool IsStealth { get; private set; }

            // per-enemy orb counter and death threshold
            public int OrbsSinceSpawn { get; private set; }
            private int deathOrbsThreshold;
            public bool IsDead { get; private set; }

            // stealth-specific state
            private int spawnTick;
            private int nextPeriodicRevealTick;
            private int periodicVisibleUntilTick;

            private List<(int x, int y)> path;
            private int pathIndex;
            private int lastTargetX;
            private int lastTargetY;

            public Follower(char glyph)
            {
                Glyph = glyph;
                moveDelayMs = 400 + Program.rng.Next(0, 400);
                lastMoveTick = Environment.TickCount - moveDelayMs;
                IsRare = false;
                IsGolden = false;
                IsWhite = false;
                IsStealth = false;

                // initialize per-enemy orb lifetime
                OrbsSinceSpawn = 0;
                deathOrbsThreshold = Program.rng != null ? Program.rng.Next(1, 6) : 5; // [1, 5]
                IsDead = false;

                spawnTick = 0;
                nextPeriodicRevealTick = 0;
                periodicVisibleUntilTick = 0;
                path = null;
                pathIndex = 0;
                lastTargetX = int.MinValue;
                lastTargetY = int.MinValue;
            }

            private void ResetOrbLifetime()
            {
                OrbsSinceSpawn = 0;
                deathOrbsThreshold = Program.rng != null ? Program.rng.Next(5, 6) : 5;
                IsDead = false;
            }

            public void MakeRare()
            {
                if (IsRare) return;
                IsRare = true;
                IsGolden = false;
                IsWhite = false;
                IsStealth = false;
                Glyph = 'X';
                moveDelayMs = Math.Max(60, moveDelayMs / 2);
                ResetOrbLifetime();
            }

            public void MakeGolden()
            {
                if (IsGolden) return;
                IsGolden = true;
                IsRare = false;
                IsWhite = false;
                IsStealth = false;
                Glyph = 'G';
                ResetOrbLifetime();
            }

            public void MakeWhite()
            {
                if (IsWhite) return;
                IsWhite = true;
                IsRare = false;
                IsGolden = false;
                IsStealth = false;
                Glyph = 'W';
                // set base speed between normal and rare (approx 2/3 of original)
                moveDelayMs = Math.Max(80, (moveDelayMs * 2) / 3);

                // mark that a white has spawned and must wait until the next orb is eaten
                Program.WhiteSpawnedWaitingForOrb = true;
                ResetOrbLifetime();
            }

            public void MakeStealth()
            {
                if (IsStealth) return;
                IsStealth = true;
                IsRare = false;
                IsGolden = false;
                IsWhite = false;
                Glyph = 'S';
                // stealth moves as fast as rare enemies
                moveDelayMs = Math.Max(60, (int)(moveDelayMs / 1.5));

                // initialize per-instance stealth state
                ResetOrbLifetime();
                spawnTick = Environment.TickCount;
                // visible for 1s on spawn
                periodicVisibleUntilTick = spawnTick + 1000;
                // next periodic reveal in 5s
                nextPeriodicRevealTick = spawnTick + 5000;
            }

            // called when player eats an orb; all followers track orbs eaten since spawn and may die
            public void OnOrbEaten()
            {
                OrbsSinceSpawn++;
                // if this reaches the random threshold, mark for removal
                if (OrbsSinceSpawn >= deathOrbsThreshold)
                {
                    IsDead = true;
                }
            }

            public void SetPosition(int x, int y)
            {
                prevX = X;
                prevY = Y;
                X = x;
                Y = y;
                path = null;
                pathIndex = 0;

                // treat SetPosition as a spawn event for lifetime tracking
                ResetOrbLifetime();

                if (IsStealth)
                {
                    spawnTick = Environment.TickCount;
                    periodicVisibleUntilTick = spawnTick + 1000;
                    nextPeriodicRevealTick = spawnTick + 5000;
                }
            }

            public void Respawn()
            {
                for (int attempt = 0; attempt < 10000; attempt++)
                {
                    int y = Program.rng.Next(0, mapLines.Length);
                    var line = mapLines[y].Replace("\r", "");
                    if (line.Length == 0) continue;
                    int x = Program.rng.Next(0, line.Length);
                    if (Program.IsWalkable(x, y) && !(Program.player != null && Program.player.X == x && Program.player.Y == y))
                    {
                        prevX = X;
                        prevY = Y;
                        X = x;
                        Y = y;
                        path = null;
                        pathIndex = 0;

                        // reset lifetime on respawn
                        ResetOrbLifetime();

                        if (IsStealth)
                        {
                            spawnTick = Environment.TickCount;
                            periodicVisibleUntilTick = spawnTick + 1000;
                            nextPeriodicRevealTick = spawnTick + 5000;
                        }
                        return;
                    }
                }

                if (Program.player != null)
                {
                    prevX = X;
                    prevY = Y;
                    X = Program.player.X;
                    Y = Program.player.Y;
                    path = null;
                    pathIndex = 0;

                    ResetOrbLifetime();
                }
            }

            public void TryStepTowards(int targetX, int targetY, HashSet<(int x, int y)> occupied)
            {
                int now = Environment.TickCount;
                int currentDelay = moveDelayMs;

                // White enemy: when inside player's radius, speed up to 150ms
                if (IsWhite)
                {
                    int distToTargetW = Math.Abs(X - targetX) + Math.Abs(Y - targetY);
                    currentDelay = distToTargetW <= Program.GoldenSlowRadius ? 150 : moveDelayMs;
                }
                // Golden enemy: special slow-radius behavior like before
                else if (IsGolden)
                {
                    int playerDelay = Program.player != null ? Program.player.GetMoveDelayMs() : moveDelayMs;
                    int baseDelay = Math.Max(40, playerDelay * 4);
                    int closeDelay = Math.Max(200, playerDelay * 6);
                    int distToTarget = Math.Abs(X - targetX) + Math.Abs(Y - targetY);
                    currentDelay = distToTarget <= Program.GoldenSlowRadius ? closeDelay : baseDelay;
                }

                // If global time slow is active, make followers effectively slower (longer required delay)
                double globalMultiplier = Program.TimeSlowActive ? 4.0 : 1.0;
                if (now - lastMoveTick < (int)(currentDelay * globalMultiplier)) return;

                bool needRecalc = path == null || lastTargetX != targetX || lastTargetY != targetY || pathIndex >= path.Count;

                if (needRecalc)
                {
                    var newPath = Program.FindPathAStar(X, Y, targetX, targetY);
                    if (newPath == null || newPath.Count == 0)
                    {
                        lastMoveTick = now;
                        lastTargetX = targetX;
                        lastTargetY = targetY;
                        return;
                    }
                    path = newPath;
                    pathIndex = 0;
                    lastTargetX = targetX;
                    lastTargetY = targetY;
                }

                int nextIdx = Math.Min(pathIndex + 1, path.Count - 1);
                if (nextIdx <= pathIndex)
                {
                    lastMoveTick = now;
                    return;
                }
                var next = path[nextIdx];

                if (occupied.Contains(next))
                {
                    var blocked = new HashSet<(int x, int y)>(occupied);
                    blocked.Remove((X, Y));
                    var alt = Program.FindPathAStar(X, Y, targetX, targetY, blocked);
                    if (alt != null && alt.Count > 1)
                    {
                        path = alt;
                        pathIndex = 0;
                        nextIdx = 1;
                        next = path[nextIdx];
                        if (occupied.Contains(next))
                        {
                            lastMoveTick = now;
                            return;
                        }
                    }
                    else
                    {
                        lastMoveTick = now;
                        return;
                    }
                }

                if (occupied.Contains((X, Y))) occupied.Remove((X, Y));
                prevX = X;
                prevY = Y;
                X = next.x;
                Y = next.y;
                occupied.Add((X, Y));

                if (path != null && pathIndex < path.Count && path[pathIndex].x == prevX && path[pathIndex].y == prevY)
                    pathIndex = nextIdx;

                lastMoveTick = now;
            }

            public void Draw()
            {
                try
                {
                    // restore previous cell
                    if (prevX >= 0 && (prevX != Program.player.X || prevY != Program.player.Y))
                    {
                        Console.SetCursorPosition(prevX, prevY);
                        char underlying = ' ';
                        if (mapLines != null && prevY >= 0 && prevY < mapLines.Length)
                        {
                            var line = mapLines[prevY].Replace("\r", "");
                            if (prevX >= 0 && prevX < line.Length)
                                underlying = line[prevX];
                        }
                        Console.Write(underlying);
                    }

                    // For stealth enemies: decide visibility based on per-instance rules
                    if (IsStealth)
                    {
                        int now = Environment.TickCount;
                        // trigger periodic reveal if it's time
                        if (now >= nextPeriodicRevealTick && periodicVisibleUntilTick < now)
                        {
                            // start a periodic visible window of 500ms
                            periodicVisibleUntilTick = now + 500;
                            // schedule next periodic reveal
                            nextPeriodicRevealTick = now + 5000;
                        }

                        // compute distance-based visibility radius that grows with orbs eaten since spawn
                        // NOTE: user requested range growth multiplied by 5 (apply multiplier only in formula)
                        int visRadius = Program.GoldenSlowRadius + (OrbsSinceSpawn * 5);

                        int dist = Math.Abs(X - Program.player.X) + Math.Abs(Y - Program.player.Y);

                        bool visibleNow = false;
                        // visible in spawn window or periodic reveal window
                        if (now <= periodicVisibleUntilTick) visibleNow = true;
                        // or visible if within growing radius
                        if (dist <= visRadius) visibleNow = true;

                        if (!visibleNow)
                        {
                            // do not draw this stealth enemy right now
                            prevX = X;
                            prevY = Y;
                            return;
                        }
                    }

                    Console.SetCursorPosition(X, Y);
                    var old = Console.ForegroundColor;
                    if (IsGolden)
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    else if (IsWhite)
                        Console.ForegroundColor = ConsoleColor.White;
                    else if (IsStealth)
                        Console.ForegroundColor = ConsoleColor.Gray;
                    else
                        Console.ForegroundColor = IsRare ? ConsoleColor.Magenta : ConsoleColor.Red;
                    Console.Write(Glyph);
                    Console.ForegroundColor = old;
                }
                catch (ArgumentOutOfRangeException)
                {
                }

                prevX = X;
                prevY = Y;
            }
        }
    }
}

// Von Marko - Unterschrift einfach mal weil.