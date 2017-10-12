using CommandLine;

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

		[Option('n', "notMigratedSqlExistsMessage", DefaultValue = "<!here|here> 未適用の migration SQL を適用してください :no_good: \n既に適用済みであれば、ファイル名と migrated_file_name が同値であるか確認し、適切に変更してください。\n", HelpText = "未適用の migration SQL が存在している場合のメッセージを指定します。未指定の場合、 デフォルトのメッセージが選択されます。")]
		public string NotMigratedSqlExistsMessage { get; set; }

		[Option("dryRun", DefaultValue = false, HelpText = "DryRun 実行します。指定された場合、 チームメンションしません。")]
		public bool DryRun { get; set; }
	}
}
