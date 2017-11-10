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
			new Regex($@"\d{{8}}(_|-)(\d{{2}}(_|-)){{0,1}}({AllNodeIdentifier}|{CommonDbNodeIdentifier}|{NewsDbNodeIdentifier})(_|-)");

		private static readonly Regex DataDbAppliedMigrationSqlRegex =
			new Regex($@"\d{{8}}(_|-)(\d{{2}}(_|-)){{0,1}}({AllNodeIdentifier}|{DataDbNodeIdentifier}|{DemoDbNodeIdentifier})(_|-)");

		private static readonly Regex MentionRegex = new Regex(@"<!(.*)\|(.*)> ");

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
			var expectedCommonDbAppliedMigrationSqls =
				migrationSqls.Where(fileName => CommonDbAppliedMigrationSqlRegex.IsMatch(fileName));
			var expectedDataDbAppliedMigrationSqls =
				migrationSqls
					.Where(fileName => DataDbAppliedMigrationSqlRegex.IsMatch(fileName))
					.ToList();

			IEnumerable<string> actualCommonDbAppliedMigrationSqls;
			var commonDbConnectionStringBuilder =
				CreateConnectionStringBuilder(options.DbHost, options.DbUser, options.DbPassword, CommonDbNodeIdentifier);
			using (var commonDbConnection = new NpgsqlConnection(commonDbConnectionStringBuilder))
			{
				actualCommonDbAppliedMigrationSqls = commonDbConnection.Query<string>(FindMigratedFileNameSql);
			}
			IEnumerable<string> actualDataDb1AppliedMigrationSqls;
			var dataDb1ConnectionStringBuilder = CreateConnectionStringBuilder(options.DbHost, options.DbUser,
				options.DbPassword, DataDbNodeIdentifier + "1");
			using (var dataDb1Connection = new NpgsqlConnection(dataDb1ConnectionStringBuilder))
			{
				actualDataDb1AppliedMigrationSqls = dataDb1Connection.Query<string>(FindMigratedFileNameSql);
			}
			IEnumerable<string> actualDataDb2AppliedMigrationSqls;
			var dataDb2ConnectionStringBuilder = CreateConnectionStringBuilder(options.DbHost, options.DbUser,
				options.DbPassword, DataDbNodeIdentifier + "2");
			using (var dataDbConnection = new NpgsqlConnection(dataDb2ConnectionStringBuilder))
			{
				actualDataDb2AppliedMigrationSqls = dataDbConnection.Query<string>(FindMigratedFileNameSql);
			}

			var commonDbNotAppliedMigrationSqls = expectedCommonDbAppliedMigrationSqls
				.Except(actualCommonDbAppliedMigrationSqls)
				.OrderBy(s => s)
				.ToList();
			var dataDb1NotAppliedMigrationSqls = expectedDataDbAppliedMigrationSqls
				.Except(actualDataDb1AppliedMigrationSqls)
				.OrderBy(s => s)
				.ToList();
			var dataDb2NotAppliedMigrationSqls = expectedDataDbAppliedMigrationSqls
				.Except(actualDataDb2AppliedMigrationSqls)
				.OrderBy(s => s)
				.ToList();

			var attachments = new List<dynamic>();

			if (commonDbNotAppliedMigrationSqls.Any())
				attachments.Add(new
				{
					color = "danger",
					title = commonDbConnectionStringBuilder.Database,
					text = string.Join(Environment.NewLine, commonDbNotAppliedMigrationSqls.Select(s => $"`{s}`")),
					mrkdwn_in = new[] {"text"}
				});
			if (dataDb1NotAppliedMigrationSqls.Any())
				attachments.Add(new
				{
					color = "danger",
					title = dataDb1ConnectionStringBuilder.Database,
					text = string.Join(Environment.NewLine, dataDb1NotAppliedMigrationSqls.Select(s => $"`{s}`")),
					mrkdwn_in = new[] {"text"}
				});
			if (dataDb2NotAppliedMigrationSqls.Any())
				attachments.Add(new
				{
					color = "danger",
					title = dataDb2ConnectionStringBuilder.Database,
					text = string.Join(Environment.NewLine, dataDb2NotAppliedMigrationSqls.Select(s => $"`{s}`")),
					mrkdwn_in = new[] {"text"}
				});

			using (var httpClient = new HttpClient())
			{
				var defaultNotMigratedSqlExistsMessage = string.Empty;
				foreach (var propertyInfo in typeof(Options).GetProperties())
					if (propertyInfo.Name == nameof(Options.NotMigratedSqlExistsMessage))
					{
						var optionAttribute = (OptionAttribute) propertyInfo.GetCustomAttributes(typeof(OptionAttribute), false).Single();
						defaultNotMigratedSqlExistsMessage = (string) optionAttribute.DefaultValue;
					}
				var text = string.Empty;
				if (commonDbNotAppliedMigrationSqls.Any()
				    || dataDb1NotAppliedMigrationSqls.Any()
				    || dataDb2NotAppliedMigrationSqls.Any())
				{
					text = string.IsNullOrEmpty(options.NotMigratedSqlExistsMessage)
						? defaultNotMigratedSqlExistsMessage
						: options.NotMigratedSqlExistsMessage.Replace("\\n", Environment.NewLine);

					if (options.DryRun)
					{
						var mentionText = MentionRegex.Match(text).Groups[2].Captures[0].Value;
						text = MentionRegex.Replace(text, $"`@{mentionText}` ");
					}

					text += $" ({options.DbHost})";
				}
				else
				{
					if (!string.IsNullOrEmpty(options.AllMigratedMessage))
						text = options.AllMigratedMessage.Replace("\\n", Environment.NewLine);
				}

				if (!string.IsNullOrEmpty(text))
				{
					var postJson = JsonConvert.SerializeObject(new
					{
						text,
						attachments
					});

					using (var content = new StringContent(postJson, Encoding.UTF8, "application/json"))
					{
						httpClient.PostAsync(options.SlackWebhookUrl, content).Wait();
					}
				}
			}

			Console.WriteLine("migration SQL が適用されているかどうかの確認を終了しました。");
			Environment.Exit(Environment.ExitCode);
		}

		private static NpgsqlConnectionStringBuilder CreateConnectionStringBuilder(string dbHost, string dbUser,
			string dbPassword, string nodeIdentifier)
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