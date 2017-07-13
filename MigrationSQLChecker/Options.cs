﻿using CommandLine;

namespace MigrationSQLChecker
{
	internal class Options
	{
		[Option('d', "migrationSqlDirectory", Required = true, HelpText = "migration SQL が格納されたディレクトリを指定します。")]
		public string MigrationSqlDirecotry { get; set; }

		[Option('h', "dbHost", Required = true, HelpText = "migration SQL が適用されているかどうか確認したい DB の IP を指定します。")]
		public string DbHost { get; set; }

		[Option('u', "dbUser", Required = true, HelpText = "migration SQL が適用されているかどうか確認したい DB の参照権限のあるユーザを指定します。")]
		public string DbUser { get; set; }

		[Option('p', "dbPassword", Required = true, HelpText = "-u または --dbUser に指定したユーザのパスワードを指定します。")]
		public string DbPassword { get; set; }

		[Option('s', "slackWebhookUrl", DefaultValue = null, HelpText = "未適用の migration SQL の一覧をポストするための Webhook Url を指定します。未指定の場合、 Slack への投稿は実行されません。")]
		public string SlackWebhookUrl { get; set; }

		[Option("dryRun", DefaultValue = false, HelpText = "DryRun 実行します。指定された場合、 チームメンションしません。")]
		public bool DryRun { get; set; }
	}
}
