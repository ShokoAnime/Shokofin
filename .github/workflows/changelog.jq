
reduce .[] as $commit (
  "";
  . +
  "### `\($commit.type)`: \($commit.subject). (\($commit.commit)) @\($commit.author.github) (date: \($commit.author.date), TZ: \($commit.author.timeZone))" +
  if $commit.isSkipCI then " (_Skip CI_)" else "" end +

  if $commit.body != null and $commit.body != "" then
    "\n\n\($commit.body | gsub("\n"; "\n"))"
  else
    ""
  end +
  "\n\n"
)
