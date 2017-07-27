using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using CommandLine;
using Dapper;
using Newtonsoft.Json;
using Npgsql;

namespace MigrationSQLChecker
{
	internal static class Program
	{
		private const string AllNodeIdentifier = "allnode";
		private const string CommonDbNodeIdentifier = "cmn";
		private const string DataDbNodeIdentifier = "data";
		private const string NewsDbNodeIdentifier = "news";
		private const string DemoDbNodeIdentifier = "demo";

		private static readonly Regex CommonDbAppliedMigrationSqlRegex =
			new Regex($@".{{8}}(_|-).{{2}}(_|-)({AllNodeIdentifier}|{CommonDbNodeIdentifier}|{NewsDbNodeIdentifier})(_|-)");

		private static readonly Regex DataDbAppliedMigrationSqlRegex =
			new Regex($@".{{8}}(_|-).{{2}}(_|-)({AllNodeIdentifier}|{DataDbNodeIdentifier}|{DemoDbNodeIdentifier})(_|-)");

		private static readonly string FindMigratedFileNameSql = @"
select
	migrated_file_name
from
	lkmigration.migration_history;
";

		private static void Main(string[] args)
		{
			AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;

			var options = new Options();
			Parser.Default.ParseArgumentsStrict(args, options, () =>
			{
				Console.WriteLine("コマンドライン引数のパースに失敗しました。コマンドライン引数を確認してください。");
				Environment.Exit(Environment.ExitCode);
			});

			Console.WriteLine("migration SQL が適用されているかどうかの確認を開始します。");

			var directoryInfo = new DirectoryInfo(options.MigrationSqlDirecotry);
			var migrationSqls = directoryInfo.GetFiles("*.sql", SearchOption.TopDirectoryOnly)
				.Select(fileInfo => fileInfo.Name)
				.ToList();
			var exceptCommonDbAppliedMigrationSqls =
				migrationSqls.Where(fileName => CommonDbAppliedMigrationSqlRegex.IsMatch(fileName));
			var exceptDataDbAppliedMigrationSqls =
				migrationSqls
					.Where(fileName => DataDbAppliedMigrationSqlRegex.IsMatch(fileName))
					.ToList();

			IEnumerable<string> actualCommonDbAppliedMigrationSqls;
			var commonDbConnectionStringBuilder = CreateConnectionStringBuilder(options.DbHost, options.DbUser, options.DbPassword, CommonDbNodeIdentifier);
			using (var commonDbConnection = new NpgsqlConnection(commonDbConnectionStringBuilder))
			{
				actualCommonDbAppliedMigrationSqls = commonDbConnection.Query<string>(FindMigratedFileNameSql);
			}
			IEnumerable<string> actualDataDb1AppliedMigrationSqls;
			var dataDb1ConnectionStringBuilder = CreateConnectionStringBuilder(options.DbHost, options.DbUser, options.DbPassword, DataDbNodeIdentifier + "1");
			using (var dataDb1Connection = new NpgsqlConnection(dataDb1ConnectionStringBuilder))
			{
				actualDataDb1AppliedMigrationSqls = dataDb1Connection.Query<string>(FindMigratedFileNameSql);
			}
			IEnumerable<string> actualDataDb2AppliedMigrationSqls;
			var dataDb2ConnectionStringBuilder = CreateConnectionStringBuilder(options.DbHost, options.DbUser, options.DbPassword, DataDbNodeIdentifier + "2");
			using (var dataDbConnection = new NpgsqlConnection(dataDb2ConnectionStringBuilder))
			{
				actualDataDb2AppliedMigrationSqls = dataDbConnection.Query<string>(FindMigratedFileNameSql);
			}

			var commonDbNotAppliedMigrationSqls = exceptCommonDbAppliedMigrationSqls.Except(actualCommonDbAppliedMigrationSqls).ToList();
			var dataDb1NotAppliedMigrationSqls = exceptDataDbAppliedMigrationSqls.Except(actualDataDb1AppliedMigrationSqls).ToList();
			var dataDb2NotAppliedMigrationSqls = exceptDataDbAppliedMigrationSqls.Except(actualDataDb2AppliedMigrationSqls).ToList();

			using (var httpClient = new HttpClient())
			{
				const string notAppliedMigrationSqlNotFoundMessage = "未適用の migration SQL はありません。";
				var postJson = JsonConvert.SerializeObject(new
				{
					text =
					$@"{
							(options.DryRun ? "`@nux-dev`" : "<!subteam^S2WPQQU2F|nux-dev>")
						} 未適用の migration SQL が存在してます。適用するか、既に適用済みであれば、 migrated_file_name を正しいものに更新してくださいね。更新しなかったら…どうなるか分かりますよね :question: :fire: :snake: ",
					attachments = new[]
					{
						new
						{
							text = $"---- {commonDbConnectionStringBuilder.Database} ----"
							       + Environment.NewLine
							       + (commonDbNotAppliedMigrationSqls.Any() ? string.Join(Environment.NewLine, commonDbNotAppliedMigrationSqls) : notAppliedMigrationSqlNotFoundMessage)
							       + Environment.NewLine
							       + $"---- {dataDb1ConnectionStringBuilder.Database} ----"
							       + Environment.NewLine
							       + (dataDb1NotAppliedMigrationSqls.Any() ? string.Join(Environment.NewLine, dataDb1NotAppliedMigrationSqls) : notAppliedMigrationSqlNotFoundMessage)
							       + Environment.NewLine
							       + $"---- {dataDb2ConnectionStringBuilder.Database} ----"
							       + Environment.NewLine
							       + (dataDb2NotAppliedMigrationSqls.Any() ? string.Join(Environment.NewLine, dataDb2NotAppliedMigrationSqls) : notAppliedMigrationSqlNotFoundMessage)
						}
					}
				});

				using (var content = new StringContent(postJson, Encoding.UTF8, "application/json"))
				{
					httpClient.PostAsync(options.SlackWebhookUrl, content).Wait();
				}
			}

			Console.WriteLine("migration SQL が適用されているかどうかの確認を終了しました。");
			Environment.Exit(Environment.ExitCode);
		}

		private static NpgsqlConnectionStringBuilder CreateConnectionStringBuilder(string dbHost, string dbUser, string dbPassword, string nodeIdentifier)
		{
			return new NpgsqlConnectionStringBuilder
			{
				Host = dbHost,
				Port = 5432,
				Database = $"lkweb_{nodeIdentifier}",
				UserName = dbUser,
				Password = dbPassword,
				Timeout = 600,
				CommandTimeout = 600,
				MinPoolSize = 1,
				MaxPoolSize = 100,
				Pooling = true,
				Enlist = true
			};
		}

		private static void LogUnhandledException(object sender, UnhandledExceptionEventArgs args)
		{
			var exception = (Exception) args.ExceptionObject;
			Console.WriteLine($@"UnhandledException caught!
Runtime terminating: {args.IsTerminating}
Exception detail:", exception);
		}
	}
}
