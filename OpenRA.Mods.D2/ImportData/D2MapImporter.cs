#region Copyright & License Information
/*
 * Copyright 2007-2019 The d2 mod Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
/*
 * Based on Dune Dynasty landscape.c (https://sourceforge.net/p/dunedynasty/dunedynasty/ci/master/tree/src/mods/landscape.c)
 * Dune Dynasty Written by:
 *   (c) David Wang <dswang@users.sourceforge.net>
 */
#endregion

using System;
using System.Drawing;
using System.IO;
using OpenRA.Mods.Common.FileFormats;
using OpenRA.Mods.D2.MathExtention;
using OpenRA.Mods.D2.MapUtils;

namespace OpenRA.Mods.D2.ImportData
{
	public class D2MapImporter
	{
		readonly Ruleset rules;
		readonly IniFile iniFile;
		readonly string tilesetName;
		readonly TerrainTile clearTile;

		Map map;
		Size mapSize;
		TileSet tileset;
		int playerCount;

		UInt16[] m;
		D2MapSeed seed;

		D2MapImporter(string filename, string tileset, Ruleset rules)
		{
			tilesetName = tileset;
			this.rules = rules;

			try
			{
				clearTile = new TerrainTile(0, 0);
				using (var stream = Game.ModData.DefaultFileSystem.Open(filename))
				{
					if (stream.Length == 0)
						throw new ArgumentException("The map is in an unrecognized format!", "filename");

					iniFile = new IniFile(stream);

					Initialize(filename);

					/* Fill map using seed value from ini file */
					FillMap();

					/* Add Additional spice fields from "Fields" value from ini file */
					AddAdditionalSpiceFields();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				map = null;
			}
		}

		public static Map Import(string filename, string mod, string tileset, Ruleset rules)
		{
			var importer = new D2MapImporter(filename, tileset, rules);
			var map = importer.map;
			if (map == null)
				return null;

			map.RequiresMod = mod;

			return map;
		}

		void Initialize(string mapFile)
		{
			/* Map is always 64x64 tiles big but only a portion of this map is
			 * displayed depending on the value of MapScale:
			 *  0 (1,1) (62,62)
			 *  1 (16,16) (47,47)
			 *  2 (21,21) (41,41)
			 */

			var mapScaleValue = iniFile.GetSection("BASIC").GetValue("MapScale", "0");
			var mapScale = 0;
			Int32.TryParse(mapScaleValue, out mapScale);

			mapSize = new Size(64, 64);
			m = new UInt16[64 * 64];

			tileset = Game.ModData.DefaultTileSets["arrakis2"];

			map = new Map(Game.ModData, tileset, mapSize.Width, mapSize.Height)
			{
				Title = Path.GetFileNameWithoutExtension(mapFile),
				Author = "Westwood Studios"
			};

			var tl = new PPos(1, 1);
			var br = new PPos(62, 62);
			if (mapScale == 1)
			{
				tl = new PPos(16, 16);
				br = new PPos(47, 47);
			}
			else if (mapScale == 2)
			{
				tl = new PPos(21, 21);
				br = new PPos(41, 41);
			}
			map.SetBounds(tl, br);

			// Get all templates from the tileset YAML file that have at least one frame and an Image property corresponding to the requested tileset
			// Each frame is a tile from the Dune 2000 tileset files, with the Frame ID being the index of the tile in the original file
			//tileSetsFromYaml = tileSet.Templates.Where(t => t.Value.Frames != null
			//	&& t.Value.Images[0].ToLower() == tilesetName.ToLower()).Select(ts => ts.Value).ToList();

			playerCount = 2;
			var players = new MapPlayers(map.Rules, playerCount);
			map.PlayerDefinitions = players.ToMiniYaml();
		}

		/*
		 * Begin ported from Dune Dynasty
		 */

		// some offsets
		readonly sbyte[] around = {
			 0,
			-1,  1,   -16, 16,
			-17, 17,  -15, 15,
			-2,  2,   -32, 32,
			-4,  4,   -64, 64,
			-30, 30,  -34, 34
		};

		readonly UInt16[,,] offsetTable = {
			{
				{0, 0, 4, 0}, {4, 0, 4, 4}, {0, 0, 0, 4}, {0, 4, 4, 4},
				{0, 0, 0, 2}, {0, 2, 0, 4}, {0, 0, 2, 0}, {2, 0, 4, 0},
				{4, 0, 4, 2}, {4, 2, 4, 4}, {0, 4, 2, 4}, {2, 4, 4, 4},
				{0, 0, 4, 4}, {2, 0, 2, 2}, {0, 0, 2, 2}, {4, 0, 2, 2},
				{0, 2, 2, 2}, {2, 2, 4, 2}, {2, 2, 0, 4}, {2, 2, 4, 4},
				{2, 2, 2, 4},
			},
			{
				{0, 0, 4, 0}, {4, 0, 4, 4}, {0, 0, 0, 4}, {0, 4, 4, 4},
				{0, 0, 0, 2}, {0, 2, 0, 4}, {0, 0, 2, 0}, {2, 0, 4, 0},
				{4, 0, 4, 2}, {4, 2, 4, 4}, {0, 4, 2, 4}, {2, 4, 4, 4},
				{4, 0, 0, 4}, {2, 0, 2, 2}, {0, 0, 2, 2}, {4, 0, 2, 2},
				{0, 2, 2, 2}, {2, 2, 4, 2}, {2, 2, 0, 4}, {2, 2, 4, 4},
				{2, 2, 2, 4},
			},
		};

		readonly int[] stepX = {
			0,    3,     6,    9,    12,   15,   18,   21,   24,   27,   30,   33,   36,   39,   42,   45,
			48,   51,    54,   57,   59,   62,   65,   67,   70,   73,   75,   78,   80,   82,   85,   87,
			89,   91,    94,   96,   98,   100,  101,  103,  105,  107,  108,  110,  111,  113,  114,  116,
			117,  118,   119,  120,  121,  122,  123,  123,  124,  125,  125,  126,  126,  126,  126,  126,
			127,  126,   126,  126,  126,  126,  125,  125,  124,  123,  123,  122,  121,  120,  119,  118,
			117,  116,   114,  113,  112,  110,  108,  107,  105,  103,  102,  100,  98,   96,   94,   91,
			89,   87,    85,   82,   80,   78,   75,   73,   70,   67,   65,   62,   59,   57,   54,   51,
			48,   45,    42,   39,   36,   33,   30,   27,   24,   21,   18,   15,   12,   9,    6,    3,
			0,    -3,   -6,   -9,   -12,  -15,  -18,  -21,  -24,  -27,  -30,  -33,  -36,  -39,  -42,  -45,
			-48,  -51,  -54,  -57,  -59,  -62,  -65,  -67,  -70,  -73,  -75,  -78,  -80,  -82,  -85,  -87,
			-89,  -91,  -94,  -96,  -98,  -100, -102, -103, -105, -107, -108, -110, -111, -113, -114, -116,
			-117, -118, -119, -120, -121, -122, -123, -123, -124, -125, -125, -126, -126, -126, -126, -126,
			-126, -126, -126, -126, -126, -126, -125, -125, -124, -123, -123, -122, -121, -120, -119, -118,
			-117, -116, -114, -113, -112, -110, -108, -107, -105, -103, -102, -100,  -98, -96,  -94,  -91,
			-89,  -87,  -85,  -82,  -80,  -78,  -75,  -73,  -70,  -67,  -65,  -62,  -59,  -57,  -54,  -51,
			-48,  -45,  -42,  -39,  -36,  -33,  -30,  -27,  -24,  -21,  -18,  -15,  -12,  -9,   -6,   -3
		};

		readonly int[] stepY = {
			127,   126,  126,  126,  126,  126,  125,  125,  124,  123,  123,  122,  121,  120,  119,  118,
			117,   116,  114,  113,  112,  110,  108,  107,  105,  103,  102,  100,  98,   96,   94,   91,
			89,    87,   85,   82,   80,   78,   75,   73,   70,   67,   65,   62,   59,   57,   54,   51,
			48,    45,   42,   39,   36,   33,   30,   27,   24,   21,   18,   15,   12,   9,    6,    3,
			0,    -3,   -6,   -9,   -12,  -15,  -18,  -21,  -24,  -27,  -30,  -33,  -36,  -39,  -42,  -45,
			-48,  -51,  -54,  -57,  -59,  -62,  -65,  -67,  -70,  -73,  -75,  -78,  -80,  -82,  -85,  -87,
			-89,  -91,  -94,  -96,  -98,  -100, -102, -103, -105, -107, -108, -110, -111, -113, -114, -116,
			-117, -118, -119, -120, -121, -122, -123, -123, -124, -125, -125, -126, -126, -126, -126, -126,
			-126, -126, -126, -126, -126, -126, -125, -125, -124, -123, -123, -122, -121, -120, -119, -118,
			-117, -116, -114, -113, -112, -110, -108, -107, -105, -103, -102, -100, -98,  -96,  -94,  -91,
			-89,  -87,  -85,  -82,  -80,  -78,  -75,  -73,  -70,  -67,  -65,  -62,  -59,  -57,  -54,  -51,
			-48,  -45,  -42,  -39,  -36,  -33,  -30,  -27,  -24,  -21,  -18,  -15,  -12,  -9,   -6,   -3,
			0,     3,    6,    9,    12,   15,   18,   21,   24,   27,   30,   33,   36,   39,   42,   45,
			48,    51,   54,   57,   59,   62,   65,   67,   70,   73,   75,   78,   80,   82,   85,   87,
			89,    91,   94,   96,   98,   100,  101,  103,  105,  107,  108,  110,  111,  113,  114,  116,
			117,   118,  119,  120,  121,  122,  123,  123,  124,  125,  125,  126,  126,  126,  126,  126
		};

		/* Initialises every fourth x/y tile in the map. */
		void MakeRoughLandscape()
		{
			byte[] memory = new byte[273];
			UInt16 i = 0;

			for (i = 0; i < 272; i++)
			{
				memory[i] = (byte)(seed.Random() & 0xF);
				if (memory[i] > 0xA)
				{
					memory[i] = 0xA;
				}
			}

			memory[272] = 0;

			i = (UInt16)((seed.Random() & 0xF) + 1);
			while (i-- != 0)
			{
				var b = seed.Random();

				for (var j = 0; j < around.Length; j++)
				{
					var index = D2MathExtention.Clamp(b + around[j], 0, 272);
					memory[index] = (byte)((memory[index] + (seed.Random() & 0xF)) & 0xF);
				}
			}

			i = (UInt16)((seed.Random() & 0x3) + 1);
			while (i-- != 0)
			{
				var b = seed.Random();

				for (var j = 0; j < around.Length; j++)
				{
					var index = D2MathExtention.Clamp(b + around[j], 0, 272);
					memory[index] = (byte)(seed.Random() & 0x3);
				}
			}

			for (UInt16 y = 0; y < 16; y++)
			{
				for (UInt16 x = 0; x < 16; x++)
				{
					var packed = PackXY((UInt16)(x * 4), (UInt16)(y * 4));
					m[packed] = memory[y * 16 + x];
				}
			}
		}

		/* Fills in the gaps in the rough landscape. */
 		void AverageRoughLandscape()
		{
			/* For each 5x5 grid with the 4 corner tiles initialised, this
			 * function takes tiles (x1,y1), (x2,y2) as described by
			 * offsetTable to create the intermediate tile.
			 * e.g. (0,0) and (4,0) are used to make (2,0).
			 */
			for (var j = 0; j < 16; j++)
			{
				for (var i = 0; i < 16; i++)
				{
					for (var k = 0; k < 21; k++)
					{
						var index = (i + 1) % 2;
						var x1 = (UInt16)(i * 4 + offsetTable[index, k, 0]);
						var y1 = (UInt16)(j * 4 + offsetTable[index, k, 1]);
						var x2 = (UInt16)(i * 4 + offsetTable[index, k, 2]);
						var y2 = (UInt16)(j * 4 + offsetTable[index, k, 3]);

						var packed1 = PackXY(x1, y1);
						var packed2 = PackXY(x2, y2);
						var packed = (UInt16)((packed1 + packed2) / 2);

						if (packed >= 64 * 64)
							continue;

						packed1 = PackXY((UInt16)(x1 & 0x3F), y1);
						packed2 = PackXY((UInt16)(x2 & 0x3F), y2);

						if (packed1 >= 64 * 64)
							throw new IndexOutOfRangeException("packed index is out of map bounds");

						var sprite1 = m[packed1];

						/* ENHANCEMENT -- use groundSpriteID=0 when
						 * out-of-bounds to generate the original maps.
						 */
						var sprite2 = (packed2 < 64 * 64) ? m[packed2] : 0;

						m[packed] = (UInt16)((sprite1 + sprite2 + 1) / 2);
					}
				}
			}
		}

		/* Average a tile and its immediate neighbours. */
		void Average()
		{
			UInt16[] currRow = new UInt16[64];
			UInt16[] prevRow = new UInt16[64];

			for (var j = 0; j < 64; j++)
			{
				var d = 64 * j;
				Array.Copy(currRow, prevRow, currRow.Length);

				for (var i = 0; i < 64; i++)
				{
					currRow[i] = (UInt16)(m[d + i]);
				}

				for (var i = 0; i < 64; i++)
				{
					UInt16 sum = 0;

					sum += (i == 0  || j == 0)  ? currRow[i] : prevRow[i - 1];
					sum += (           j == 0)  ? currRow[i] : prevRow[i];
					sum += (i == 63 || j == 0)  ? currRow[i] : prevRow[i + 1];
					sum += (i == 0)             ? currRow[i] : currRow[i - 1];
					sum +=                        currRow[i];
					sum += (i == 63)            ? currRow[i] : currRow[i + 1];
					sum += (i == 0  || j == 63) ? currRow[i] : (UInt16)(m[d + i + 63]);
					sum += (           j == 63) ? currRow[i] : (UInt16)(m[d + i + 64]);
					sum += (i == 63 || j == 63) ? currRow[i] : (UInt16)(m[d + i + 65]);

					m[d + i] = (UInt16)(sum / 9);
				}
			}
		}

		/* Converts random values into landscape types. */
		void DetermineLandscapeTypes()
		{
			var spriteID1 = (UInt16)(seed.Random() & 0xF);
			if (spriteID1 < 0x8)
				spriteID1 = 0x8;
			if (spriteID1 > 0xC)
				spriteID1 = 0xC;

			var spriteID2 = (UInt16)(seed.Random() & 0x3) - 1;
			if (spriteID2 > spriteID1 - 3)
				spriteID2 = spriteID1 - 3;

			for (var i = 0; i < 64 * 64; i++)
			{
				var spriteID = m[i];
				var lst =
					(spriteID >  spriteID1 + 4) ? D2MapUtils.RoughTile
					: (spriteID >= spriteID1) ? D2MapUtils.RockTile
					: (spriteID <= spriteID2) ? D2MapUtils.DuneTile
					: D2MapUtils.SandTile;

				m[i] = lst;
			}
		}

		/* Adding Spice related functions */
		public static UInt16 PackXY(UInt16 x, UInt16 y)
		{
			return (UInt16)((y << 6) | x);
		}

		public static UInt16 PackedX(UInt16 packed)
		{
			return (UInt16)(packed & 0x3F);
		}

		public static UInt16 PackedY(UInt16 packed)
		{
			return (UInt16)((packed >> 6) & 0x3F);
		}

		/* x and y multiplied by 16 */
		struct TilePos
		{
			public int X;
			public int Y;

			public TilePos(int x, int y)
			{
				this.X = x;
				this.Y = y;
			}
		}

		TilePos MoveByRandom(TilePos pos, int distance)
		{
			if (distance == 0)
				return pos;

			var newDistance = seed.Random();
			while (newDistance > distance)
				newDistance /= 2;

			var orient256 = seed.Random();
			var x = pos.X + ((stepX[orient256] * newDistance * 16) / 128);
			var y = pos.Y - ((stepY[orient256] * newDistance * 16) / 128);

			/* Out of map size check: 64 * 256 = 16384 */
			if (x > 16384 || y > 16384)
				return pos;

			x = (x & 0xff00) | 0x80;
			y = (y & 0xff00) | 0x80;

			return new TilePos(x, y);
		}

		bool IsOutOfMap(CPos pos)
		{
			return (pos.X < 0 || pos.X >= map.MapSize.X || pos.Y < 0 || pos.Y >= map.MapSize.Y);
		}

		/* Creates a spice field of the given radius at the given location. */
		void CreateSpiceField(CPos pos, int radius, bool centerIsThickSpice)
		{
			var radius2 = radius * radius;

			for (var offsetX = -radius; offsetX <= radius; offsetX++)
			{
				var offsetX2 = offsetX * offsetX;

				for (var offsetY = -radius; offsetY <= radius; offsetY++)
				{
					var offsetY2 = offsetY * offsetY;

					var coord = new CPos(pos.X + offsetX, pos.Y + offsetY);

					if (!IsOutOfMap(coord))
					{
						var packed = PackXY((ushort)coord.X, (ushort)coord.Y);
						var tile = m[packed];

						if (CanBecomeSpice(tile))
						{
							if (((radius > 2) && (offsetX2 + offsetY2 <= radius2))
								|| (offsetX2 + offsetY2 < radius2))
							{
								if (centerIsThickSpice && (offsetX == 0) && (offsetY == 0))
								{
									map.Resources[coord] = new ResourceTile(1, 2);
								}
								else
								{
									if (map.Resources[coord].Index == 2)
									{
										map.Resources[coord] = new ResourceTile(1, 2);
									}
									else
									{
										if (tile == D2MapUtils.SandTile)
										{
											map.Resources[coord] = new ResourceTile(1, 1);
										}
									}
								}
							}
						}
					}
				}
			}
		}

		public static bool CanBecomeSpice(ushort tileType)
		{
			return (tileType == D2MapUtils.SandTile || tileType == D2MapUtils.DuneTile);
		}

		/* Adds spice fields to the map. */
		void AddSpice(uint minSpiceFields, uint maxSpiceFields)
		{
			const uint maxCount = 65535;
			var count = 0;
			var i = 0L;
			
			/* ENHANCEMENT: spice field controls. */
			if ((minSpiceFields == 0) && (maxSpiceFields == 0))
			{
				i = seed.Random() & 0x2F;
			}
			else
			{
				var a = Math.Min(minSpiceFields, 255);
				var b = Math.Min(maxSpiceFields, 255);
				minSpiceFields = Math.Min(a, b);
				maxSpiceFields = Math.Max(a, b);
				var range = (maxSpiceFields - minSpiceFields + 1);

				i = (int)(seed.Random()) * range / 256 + minSpiceFields;
			}

			while (i-- != 0)
			{
				UInt16 y = 0;
				UInt16 x = 0;

				while (true)
				{
					y = (UInt16)(seed.Random() & 0x3F);
					x = (UInt16)(seed.Random() & 0x3F);
					
					var tile = m[PackXY(x, y)];

					if (CanBecomeSpice(tile))
					{
						break;
					}

					/* ENHANCEMENT: ensure termination. */
					count++;
					if (count > maxCount)
					{
						return;
					}
				}

				var x1 = ((x & 0x3F) << 8) | 0x80;
				var y1 = ((y & 0x3F) << 8) | 0x80;

				var j = (UInt16)seed.Random() & 0x1F;

				while (j-- != 0)
				{
					CPos coord;

					while (true)
					{
						var dist = seed.Random() & 0x3F;

						var pos = MoveByRandom(new TilePos(x1, y1), dist);

						var x2 = (pos.X >> 8) & 0x3F;
						var y2 = (pos.Y >> 8) & 0x3F;

						coord = new CPos(x2, y2);

						if (!IsOutOfMap(coord))
						{
							break;
						}
					}

					var tile = m[PackXY((ushort)coord.X, (ushort)coord.Y)];

					if (map.Resources[coord].Type == 1)
					{
						CreateSpiceField(coord, 2, true);
					}
					else
					{
						CreateSpiceField(coord, 1, true);
					}
				}
			}
		}

		/* Smooths the boundaries between different landscape types. */
		void Smooth()
		{
			for (UInt16 y = 0; y < 64; y++)
			{
				for (UInt16 x = 0; x < 64; x++)
				{
					var index = y * 64 + x;
					var tile = m[index];

					if (tile == D2MapUtils.RockTile || tile == D2MapUtils.DuneTile || tile == D2MapUtils.RoughTile)
					{
						m[index] = D2MapUtils.SmoothTileTypeForPos(m, 64, 64, x, y);
					}
				}
			}
		}

		void FillTiles()
		{
			for (UInt16 y = 0; y < 64; y++)
			{
				for (UInt16 x = 0; x < 64; x++)
				{
					var tile = m[PackXY(x, y)];
					map.Tiles[new CPos(x, y)] = new TerrainTile(tile, 0);
				}
			}
		}

		void CreateLandscape(UInt32 seed, uint minSpiceFields, uint maxSpiceFields)
		{
			this.seed = new D2MapSeed(seed);

			/* Place random data on a 4x4 grid. */
			MakeRoughLandscape();

			/* Average around the 4x4 grid. */
			AverageRoughLandscape();

			/* Average each tile with its neighbours. */
			Average();

			/* Filter each tile to determine its final type. */
			DetermineLandscapeTypes();

			/* Add some spice. */
			AddSpice(minSpiceFields, maxSpiceFields);

			/* Make everything smoother and use the right sprite indexes. */
			Smooth();

			/* Fill Map.Tiles */
			FillTiles();
		}

		/*
		 * End ported from Dune Dynasty
		 */

		void FillMap()
		{
			var seedStr = iniFile.GetSection("MAP").GetValue("Seed", "0");
			var seedValue = 0U;
			UInt32.TryParse(seedStr, out seedValue);

			CreateLandscape(seedValue, 0, 0);

			/*
				// Actors
				if (ActorDataByActorCode.ContainsKey(tileSpecialInfo))
				{
					var kvp = ActorDataByActorCode[tileSpecialInfo];
					if (!rules.Actors.ContainsKey(kvp.First.ToLower()))
						throw new InvalidOperationException("Actor with name {0} could not be found in the rules YAML file!".F(kvp.First));

					var a = new ActorReference(kvp.First)
					{
						new LocationInit(locationOnMap),
						new OwnerInit(kvp.Second)
					};

					map.ActorDefinitions.Add(new MiniYamlNode("Actor" + map.ActorDefinitions.Count, a.Save()));

					if (kvp.First == "mpspawn")
						playerCount++;
				}
			}
			*/
		}

		void AddAdditionalSpiceFields()
		{
			var fieldStr = iniFile.GetSection("MAP").GetValue("Field", "");
			var fields = fieldStr.Split(',');
			foreach (var field in fields)
			{
				var str = field.Trim();
				UInt16 fieldPacked = 0;
				UInt16.TryParse(str, out fieldPacked);

				var x = PackedX(fieldPacked);
				var y = PackedY(fieldPacked);

				CPos pos = new CPos(x, y);

				CreateSpiceField(pos, 5, true);
			}
		}
	}
}
