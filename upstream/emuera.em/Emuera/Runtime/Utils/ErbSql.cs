//統合_netEmuera: SQL_* 用の SQLite バックエンド。
//DB の実体は sav/temp_db/ に置き、セーブ時にセーブデータの隣へコピー、ロード時に書き戻す。
//起動時に temp_db を作り直すので、セーブせずに終了した変更は残らない。
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Utils;

internal static class ErbSql
{
	static SqliteConnection connection;
	//SqliteCommand を破棄すると配下のリーダーも閉じられるので、コマンドはリーダーと同じ寿命で持つ
	static readonly Dictionary<long, (SqliteCommand Command, SqliteDataReader Reader)> readers = [];

	// SQL プラグイン互換 (SQL_CONNECT "名前" [, "Data Source=..."]) の接続プール。
	// 1 引数版 (SQL_EXECUTE_*) は「最後に開いた接続」を使う
	static readonly Dictionary<string, SqliteConnection> namedConnections = new(StringComparer.OrdinalIgnoreCase);
	static string lastConnectionName;
	static long nextReaderId = 1;

	static string TempDir => Config.Config.SavDir + "temp_db" + Path.DirectorySeparatorChar;

	public static void ConnectionOpen(string name)
	{
		Directory.CreateDirectory(TempDir);

		CloseAll();
		SqliteConnectionStringBuilder builder = new() { DataSource = TempDir + name + ".db" };
		connection = new SqliteConnection(builder.ConnectionString);
		connection.Open();

		//ゲームのセーブ単位でしか永続化しないので、journal と fsync は落とす
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA journal_mode = OFF;PRAGMA synchronous = OFF;";
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// 起動時に前回の作業用DBを捨てる。
	/// ここは SQL_* を使わないゲームでも必ず通るので、失敗しても起動は止めない
	/// （SQLite のアセンブリが無い構成や、DBファイルが掴まれている場合がある）。
	/// </summary>
	public static void SetUpTempDB()
	{
		if (!Directory.Exists(TempDir))
			return;
		try
		{
			CloseAll();
			Directory.Delete(TempDir, true);
			Directory.CreateDirectory(TempDir);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException or TypeLoadException)
		{
		}
	}

	static void CloseAll()
	{
		foreach (long id in new List<long>(readers.Keys))
			CloseReader(id);
		foreach (string name in new List<string>(namedConnections.Keys))
			Disconnect(name);
		//一度も開いていなければ SQLite に触らない
		if (connection == null)
			return;
		connection.Close();
		connection = null;
		SqliteConnection.ClearAllPools();
	}

	public static void Save(string destDirPath)
	{
		if (!Directory.Exists(TempDir))
			return;
		Directory.CreateDirectory(destDirPath);
		foreach (FileInfo dbFile in new DirectoryInfo(TempDir).EnumerateFiles())
			dbFile.CopyTo(Path.Combine(destDirPath, dbFile.Name), true);
	}

	public static void Load(string srcDirPath)
	{
		SetUpTempDB();
		if (!Directory.Exists(srcDirPath))
			return;
		Directory.CreateDirectory(TempDir);
		foreach (FileInfo dbFile in new DirectoryInfo(srcDirPath).EnumerateFiles())
			dbFile.CopyTo(Path.Combine(TempDir, dbFile.Name), true);
	}

	static SqliteConnection RequireConnection(string methodName)
	{
		return connection ?? throw new CodeEE(string.Format(trerror.SqlNoConnection.Text, methodName));
	}

	static SqliteDataReader RequireReader(string methodName, long readerID)
	{
		if (!readers.TryGetValue(readerID, out var entry))
			throw new CodeEE(string.Format(trerror.SqlNoReader.Text, methodName, readerID));
		return entry.Reader;
	}

	static void CloseReader(long readerID)
	{
		if (!readers.TryGetValue(readerID, out var old))
			return;
		old.Reader.Dispose();
		old.Command.Dispose();
		readers.Remove(readerID);
	}

	public static void ExecuteReader(string methodName, long readerID, string sql)
	{
		SqliteCommand command = RequireConnection(methodName).CreateCommand();
		command.CommandText = sql;
		//前のリーダーは開きっぱなしにしない
		CloseReader(readerID);
		readers[readerID] = (command, command.ExecuteReader());
	}

	public static object ExecuteScalar(string methodName, string sql)
	{
		using SqliteCommand command = RequireConnection(methodName).CreateCommand();
		command.CommandText = sql;
		return command.ExecuteScalar();
	}

	public static void ExecuteNonQuery(string methodName, string sql)
	{
		using SqliteCommand command = RequireConnection(methodName).CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	public static bool ReaderRead(string methodName, long readerID) => RequireReader(methodName, readerID).Read();

	/// <summary>
	/// 列の型が INTEGER でないことがある (TEXT 属性の表に数字を入れた等)。
	/// GetInt64 は型違いで例外を投げるので、値から寄せる。
	/// </summary>
	public static long ReaderGetLong(string methodName, long readerID, int index)
	{
		SqliteDataReader reader = RequireReader(methodName, readerID);
		if (reader.FieldCount <= index || reader.IsDBNull(index))
			return 0;
		object value = reader.GetValue(index);
		if (value is long l)
			return l;
		if (value is double d)
			return (long)d;
		return long.TryParse(Convert.ToString(value), out long parsed) ? parsed : 0;
	}

	public static string ReaderGetString(string methodName, long readerID, int index)
	{
		SqliteDataReader reader = RequireReader(methodName, readerID);
		//NULL は空文字。数値の列でも落ちないように文字列化して返す
		if (reader.FieldCount <= index || reader.IsDBNull(index))
			return "";
		return Convert.ToString(reader.GetValue(index)) ?? "";
	}

	public static bool ReaderIsNull(string methodName, long readerID, int index)
	{
		SqliteDataReader reader = RequireReader(methodName, readerID);
		//列が無い場合も NULL 扱いにする（.netEmuera と同じ）
		return reader.FieldCount <= index || reader.IsDBNull(index);
	}

	#region SQL プラグイン互換
	// ここから下は「SQL.dll」系プラグインの名前・引数に合わせた入口。
	// ゲーム側は SQL_CONNECT "名前" [, "Data Source=..."] で接続を作り、
	// SQL_P_EXECUTE_* / SQL_EXECUTE_* の 1 引数版で「最後に開いた接続」を使う。
	// 実体は上と同じ SQLite なので、セーブへの同梱もそのまま効く。

	/// <summary>
	/// SQL_CONNECT "名前" [, "Data Source=..."]。
	/// 接続文字列を省いたときは sav/temp_db/名前.db を使う (エンジン内蔵版と同じ置き場所)。
	/// 相対パスはゲームフォルダ基準。ゲーム同梱の .db をそのまま開く。
	/// </summary>
	public static void Connect(string name, string connectionString)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new CodeEE(trerror.SqlNoConnection.Text);
		Disconnect(name);

		string path = ResolveDataSource(name, connectionString);
		string dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);

		SqliteConnection conn;
		try
		{
			conn = OpenReadWrite(path);
		}
		catch (SqliteException)
		{
			// 読み取り専用の場所 (ゲームフォルダが書き込み不可) に置かれた DB は
			// 作業用コピーへ退避して開く。読めれば遊べるので、ここで止めるより良い
			Directory.CreateDirectory(TempDir);
			string copy = TempDir + name + ".db";
			if (!System.IO.File.Exists(copy))
				System.IO.File.Copy(path, copy);
			conn = OpenReadWrite(copy);
		}

		namedConnections[name] = conn;
		lastConnectionName = name;

		using SqliteCommand command = conn.CreateCommand();
		command.CommandText = "PRAGMA journal_mode = OFF;PRAGMA synchronous = OFF;";
		try { command.ExecuteNonQuery(); } catch (SqliteException) { }
	}

	static SqliteConnection OpenReadWrite(string path)
	{
		SqliteConnectionStringBuilder builder = new() { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate };
		SqliteConnection conn = new(builder.ConnectionString);
		conn.Open();
		return conn;
	}

	static string ResolveDataSource(string name, string connectionString)
	{
		string path = null;
		if (!string.IsNullOrWhiteSpace(connectionString))
		{
			foreach (string part in connectionString.Split(';'))
			{
				int eq = part.IndexOf('=');
				if (eq <= 0)
					continue;
				string key = part[..eq].Trim();
				if (!key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) && !key.Equals("DataSource", StringComparison.OrdinalIgnoreCase))
					continue;
				path = part[(eq + 1)..].Trim().Trim('"');
				break;
			}
		}
		if (string.IsNullOrEmpty(path))
			return TempDir + name + ".db";
		if (!Path.IsPathRooted(path))
			path = Path.Combine(Program.ExeDir, path.Replace('\\', Path.DirectorySeparatorChar));
		return Path.GetFullPath(path);
	}

	public static void Disconnect(string name)
	{
		if (string.IsNullOrEmpty(name) || !namedConnections.TryGetValue(name, out SqliteConnection conn))
			return;
		namedConnections.Remove(name);
		if (string.Equals(lastConnectionName, name, StringComparison.OrdinalIgnoreCase))
			lastConnectionName = null;
		try
		{
			conn.Close();
		}
		catch (SqliteException)
		{
		}
		conn.Dispose();
		SqliteConnection.ClearAllPools();
	}

	static SqliteConnection RequireNamed(string methodName, string name)
	{
		if (string.IsNullOrEmpty(name) || !namedConnections.TryGetValue(name, out SqliteConnection conn))
			throw new CodeEE(string.Format(trerror.SqlNoConnection.Text, methodName));
		return conn;
	}

	static SqliteConnection DefaultConnection(string methodName)
	{
		if (!string.IsNullOrEmpty(lastConnectionName) && namedConnections.TryGetValue(lastConnectionName, out SqliteConnection conn))
			return conn;
		// 1 つしか開いていないならそれを使う (名前を省いた SQL_EXECUTE_* 用)
		if (namedConnections.Count == 1)
			foreach (SqliteConnection only in namedConnections.Values)
				return only;
		return RequireConnection(methodName);
	}

	static void BindParameters(SqliteCommand command, IReadOnlyList<object> parameters)
	{
		if (parameters == null)
			return;
		for (int i = 0; i < parameters.Count; i++)
			command.Parameters.AddWithValue("@" + i, parameters[i] ?? DBNull.Value);
	}

	public static long PluginReader(string methodName, string name, string sql, IReadOnlyList<object> parameters)
	{
		SqliteConnection conn = string.IsNullOrEmpty(name) ? DefaultConnection(methodName) : RequireNamed(methodName, name);
		long id = nextReaderId++;
		SqliteCommand command = conn.CreateCommand();
		command.CommandText = sql;
		BindParameters(command, parameters);
		readers[id] = (command, command.ExecuteReader());
		return id;
	}

	public static long PluginNonQuery(string methodName, string name, string sql, IReadOnlyList<object> parameters)
	{
		SqliteConnection conn = string.IsNullOrEmpty(name) ? DefaultConnection(methodName) : RequireNamed(methodName, name);
		using SqliteCommand command = conn.CreateCommand();
		command.CommandText = sql;
		BindParameters(command, parameters);
		return command.ExecuteNonQuery();
	}

	public static object PluginScalar(string methodName, string name, string sql, IReadOnlyList<object> parameters)
	{
		SqliteConnection conn = string.IsNullOrEmpty(name) ? DefaultConnection(methodName) : RequireNamed(methodName, name);
		using SqliteCommand command = conn.CreateCommand();
		command.CommandText = sql;
		BindParameters(command, parameters);
		return command.ExecuteScalar();
	}

	public static void CloseReaderById(string methodName, long readerID) => CloseReader(readerID);

	/// <summary>
	/// SQL_IMPORT_MAP_XML の実体。&lt;map&gt;&lt;p&gt;&lt;k&gt;..&lt;/k&gt;&lt;v&gt;..&lt;/v&gt;&lt;/p&gt;... を
	/// (k,v) の表へ入れ直す。TW の翻訳テーブルがこれで作られる。
	/// 毎回作り直すので、XML を差し替えれば次の起動で反映される。
	/// </summary>
	public static long ImportMapXml(string methodName, string name, string table, string xmlPath)
	{
		SqliteConnection conn = string.IsNullOrEmpty(name) ? DefaultConnection(methodName) : RequireNamed(methodName, name);
		string path = xmlPath ?? string.Empty;
		if (!Path.IsPathRooted(path))
			path = Path.Combine(Program.ExeDir, path.Replace('\\', Path.DirectorySeparatorChar));
		if (!System.IO.File.Exists(path))
			throw new CodeEE(string.Format(trerror.SqlNoConnection.Text, methodName) + " (" + xmlPath + ")");

		string quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
		using (SqliteCommand create = conn.CreateCommand())
		{
			create.CommandText = $"CREATE TABLE IF NOT EXISTS {quoted} (k TEXT PRIMARY KEY, v TEXT);";
			create.ExecuteNonQuery();
		}
		using (SqliteTransaction transaction = conn.BeginTransaction())
		using (SqliteCommand insert = conn.CreateCommand())
		{
			insert.Transaction = transaction;
			insert.CommandText = $"INSERT OR REPLACE INTO {quoted} (k, v) VALUES (@0, @1);";
			SqliteParameter key = insert.Parameters.Add("@0", SqliteType.Text);
			SqliteParameter value = insert.Parameters.Add("@1", SqliteType.Text);
			int count = 0;
			try
			{
				using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(path);
				string k = null, v = null;
				string current = null;
				while (reader.Read())
				{
					if (reader.NodeType == System.Xml.XmlNodeType.Element)
					{
						current = reader.Name;
						if (current == "p")
						{
							k = null;
							v = null;
						}
						else if (current == "k" || current == "v")
						{
							string text = reader.ReadElementContentAsString();
							if (current == "k")
								k = text;
							else
								v = text;
							current = null;
							if (k != null && v != null)
							{
								key.Value = k;
								value.Value = v;
								insert.ExecuteNonQuery();
								count++;
								k = null;
								v = null;
							}
						}
					}
				}
			}
			finally
			{
				transaction.Commit();
			}
			return count;
		}
	}
	#endregion
}
