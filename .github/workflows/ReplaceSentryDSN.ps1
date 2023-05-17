Param(
    [string] $dsn = "%SENTRY_DSN%"
)

$filename = "./Shokofin/Configuration/SentryConfiguration.cs"
$searchString = "%SENTRY_DSN%"

(Get-Content $filename) | ForEach-Object {
    $_ -replace $searchString, $dsn
} | Set-Content $filename
