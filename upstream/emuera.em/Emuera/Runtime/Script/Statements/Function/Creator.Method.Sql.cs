//統合_netEmuera: SQL_* の式中関数。実体は Runtime/Utils/ErbSql.cs。
//
//引数の形は 2 系統ある。どちらも同じ関数名で受ける。
//  エンジン内蔵版 : SQL_CONNECTION_OPEN(dsn) / SQL_EXECUTE_SCALAR_LONG(sql)
//  SQL.dll 互換版 : SQL_CONNECT(名前[, dsn]) / SQL_EXECUTE_SCALAR_LONG([名前,] sql)
//                   SQL_P_EXECUTE_*(名前, sql, @0, @1, ...)
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MinorShift.Emuera.GameData.Function;

internal static partial class FunctionMethodCreator
{
	#region 統合_netEmuera_SQL

	public sealed class SqlConnectionOpenMethod : FunctionMethod
	{
		public SqlConnectionOpenMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(string)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			ErbSql.ConnectionOpen(arguments[0].GetStrValue(exm));
			return 1;
		}
	}

	public sealed class SqlExecuteReaderMethod : FunctionMethod
	{
		public SqlExecuteReaderMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				// エンジン内蔵版: SQL_EXECUTE_READER(readerID, sql)
				new ArgTypeList { ArgTypes = { ArgType.Int, ArgType.String } },
				// SQL.dll 互換: SQL_EXECUTE_READER([接続名,] sql) → 新しいリーダー ID を返す
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.VariadicAny }, OmitStart = 1 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments.Count > 1 && arguments[0].GetOperandType() != typeof(string))
			{
				ErbSql.ExecuteReader(Name, arguments[0].GetIntValue(exm), arguments[1].GetStrValue(exm));
				return 1;
			}
			string name = arguments.Count > 1 ? arguments[0].GetStrValue(exm) : null;
			AExpression sql = arguments.Count > 1 ? arguments[1] : arguments[0];
			return ErbSql.PluginReader(Name, name, sql.GetStrValue(exm), null);
		}
	}

	public sealed class SqlExecuteScalerLongMethod : FunctionMethod
	{
		public SqlExecuteScalerLongMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.VariadicAny }, OmitStart = 1 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments.Count > 1 ? arguments[0].GetStrValue(exm) : null;
			AExpression sql = arguments.Count > 1 ? arguments[1] : arguments[0];
			object value = ErbSql.PluginScalar(Name, name, sql.GetStrValue(exm), null);
			//結果なし・NULL は 0 にする
			return value == null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
		}
	}

	public sealed class SqlExecuteScalerStringMethod : FunctionMethod
	{
		public SqlExecuteScalerStringMethod()
		{
			ReturnType = typeof(string);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.VariadicAny }, OmitStart = 1 },
			];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments.Count > 1 ? arguments[0].GetStrValue(exm) : null;
			AExpression sql = arguments.Count > 1 ? arguments[1] : arguments[0];
			object value = ErbSql.PluginScalar(Name, name, sql.GetStrValue(exm), null);
			return value == null || value is DBNull ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
		}
	}

	public sealed class SqlExecuteNonQueryMethod : FunctionMethod
	{
		public SqlExecuteNonQueryMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.VariadicAny }, OmitStart = 1 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments.Count > 1 ? arguments[0].GetStrValue(exm) : null;
			AExpression sql = arguments.Count > 1 ? arguments[1] : arguments[0];
			return ErbSql.PluginNonQuery(Name, name, sql.GetStrValue(exm), null);
		}
	}

	/// <summary>
	/// 次の行へ進む。.netEmuera に合わせて、行があったとき 0・尽きたとき 1 を返す
	/// （戻り値の意味が他の SQL_* と逆なので注意）
	/// </summary>
	public sealed class SqlReaderReadMethod : FunctionMethod
	{
		public SqlReaderReadMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(long)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return ErbSql.ReaderRead(Name, arguments[0].GetIntValue(exm)) ? 0 : 1;
		}
	}

	public sealed class SqlReaderGetLongMethod : FunctionMethod
	{
		public SqlReaderGetLongMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(long), typeof(long)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return ErbSql.ReaderGetLong(Name, arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
		}
	}

	public sealed class SqlReaderGetStringMethod : FunctionMethod
	{
		public SqlReaderGetStringMethod()
		{
			ReturnType = typeof(string);
			argumentTypeArray = [typeof(long), typeof(long)];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return ErbSql.ReaderGetString(Name, arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
		}
	}

	public sealed class SqlReaderIsNullMethod : FunctionMethod
	{
		public SqlReaderIsNullMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(long), typeof(long)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return ErbSql.ReaderIsNull(Name, arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm)) ? 1 : 0;
		}
	}

	#region SQL.dll 互換
	/// <summary>
	/// 可変長引数を SQL のパラメータ (@0, @1, ...) 用の値へ落とす。
	/// 数値は long、文字列は string のまま渡す (SQLite 側の型親和性で比較される)。
	/// </summary>
	static List<object> CollectParameters(ExpressionMediator exm, List<AExpression> arguments, int skip)
	{
		List<object> list = new((arguments.Count > skip) ? arguments.Count - skip : 0);
		for (int i = skip; i < arguments.Count; i++)
		{
			if (arguments[i] == null)
			{
				list.Add(null);
				continue;
			}
			list.Add(arguments[i].GetOperandType() == typeof(string)
				? arguments[i].GetStrValue(exm)
				: arguments[i].GetIntValue(exm));
		}
		return list;
	}

	/// <summary>SQL_CONNECT "名前" [, "Data Source=..."]</summary>
	public sealed class SqlConnectMethod : FunctionMethod
	{
		public SqlConnectMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.String }, OmitStart = 1 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(exm);
			string connectionString = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : null;
			ErbSql.Connect(name, connectionString);
			return 1;
		}
	}

	public sealed class SqlDisconnectMethod : FunctionMethod
	{
		public SqlDisconnectMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(string)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			ErbSql.Disconnect(arguments[0].GetStrValue(exm));
			return 1;
		}
	}

	/// <summary>SQL_P_EXECUTE_NONQUERY "名前", sql, パラメータ...</summary>
	public sealed class SqlParamNonQueryMethod : FunctionMethod
	{
		public SqlParamNonQueryMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicAny }, OmitStart = 2 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
			=> ErbSql.PluginNonQuery(Name, arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), CollectParameters(exm, arguments, 2));
	}

	public sealed class SqlParamScalarLongMethod : FunctionMethod
	{
		public SqlParamScalarLongMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicAny }, OmitStart = 2 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			object value = ErbSql.PluginScalar(Name, arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), CollectParameters(exm, arguments, 2));
			return value == null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
		}
	}

	public sealed class SqlParamScalarStringMethod : FunctionMethod
	{
		public SqlParamScalarStringMethod()
		{
			ReturnType = typeof(string);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicAny }, OmitStart = 2 },
			];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			object value = ErbSql.PluginScalar(Name, arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), CollectParameters(exm, arguments, 2));
			return value == null || value is DBNull ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
		}
	}

	public sealed class SqlParamReaderMethod : FunctionMethod
	{
		public SqlParamReaderMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArrayEx = [
				new ArgTypeList { ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicAny }, OmitStart = 2 },
			];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
			=> ErbSql.PluginReader(Name, arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), CollectParameters(exm, arguments, 2));
	}

	public sealed class SqlCloseReaderMethod : FunctionMethod
	{
		public SqlCloseReaderMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(long)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			ErbSql.CloseReaderById(Name, arguments[0].GetIntValue(exm));
			return 0;
		}
	}

	/// <summary>
	/// SQL_IMPORT_MAP_XML "接続名", "テーブル名", "XML パス"
	/// &lt;map&gt;&lt;p&gt;&lt;k&gt;..&lt;/k&gt;&lt;v&gt;..&lt;/v&gt;&lt;/p&gt;...&lt;/map&gt; を (k,v) の表へ流し込む。
	/// TW の翻訳テーブル (CSV_MAP / TASTE_MAP) がこれで作られる。
	/// </summary>
	public sealed class SqlImportMapXmlMethod : FunctionMethod
	{
		public SqlImportMapXmlMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(string), typeof(string), typeof(string)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return ErbSql.ImportMapXml(
				Name,
				arguments[0].GetStrValue(exm),
				arguments[1].GetStrValue(exm),
				arguments[2].GetStrValue(exm));
		}
	}
	#endregion
	#endregion
}
