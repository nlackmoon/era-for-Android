// EE (Emuera 拡張) のうち、TW などが起動時・描画時に必ず通る関数の受け皿。
//
// どれも「無いと 1 行でも書かれた時点でエラーになる」種類のもの。
// 画面に出す情報ではないので、値は安全側 (0 / 現在時刻) に寄せてある。
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.GameData.Function;

internal static partial class FunctionMethodCreator
{
	#region EE 互換の足りない関数

	/// <summary>
	/// GETPLATFORM()。0 が「ブラウザ / 特殊環境」で、TW は 0 のときだけ
	/// タイトルで DEMO を自動再生する。アプリは通常環境なので 1 を返す。
	/// </summary>
	public sealed class GetPlatformMethod : FunctionMethod
	{
		public GetPlatformMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments) => 1;
	}

	/// <summary>
	/// GETANIMETIMER()。アニメーション用の経過ミリ秒。
	/// 実時間を返しておけば、待ち時間の計算がそのまま動く。
	/// </summary>
	public sealed class GetAnimeTimerMethod : FunctionMethod
	{
		static readonly long origin = Environment.TickCount64;
		public GetAnimeTimerMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
			=> Environment.TickCount64 - origin;
	}

	/// <summary>
	/// GETSOUNDORBGMINFO(channel, kind)。この移植版は音を鳴らさないので常に 0。
	/// 0 は「再生していない」なので、待ちループに入らず素通りする。
	/// </summary>
	public sealed class GetSoundOrBgmInfoMethod : FunctionMethod
	{
		public GetSoundOrBgmInfoMethod()
		{
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(long), typeof(long)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments) => 0;
	}

	/// <summary>
	/// UNCHECKED_* : 桁あふれを検査せずに計算する版。
	/// TW はハッシュ計算に使うので、C# の unchecked と同じ「巻き戻し」で合っている。
	/// </summary>
	public class UncheckedBinaryMethod : FunctionMethod
	{
		readonly char op;
		public UncheckedBinaryMethod(char op)
		{
			this.op = op;
			ReturnType = typeof(long);
			argumentTypeArray = [typeof(long), typeof(long)];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long a = arguments[0].GetIntValue(exm);
			long b = arguments[1].GetIntValue(exm);
			unchecked
			{
				switch (op)
				{
					case '+': return a + b;
					case '-': return a - b;
					case '*': return a * b;
					// 0 除算でゲームを止めない (EE 側も例外にしない)
					case '/': return b == 0 ? 0 : a / b;
					case '%': return b == 0 ? 0 : a % b;
				}
			}
			return 0;
		}
	}
	#endregion
}
