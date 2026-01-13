group_by(.simple_type) |

sort_by(.[0].simple_type | ({ "feat": 0, "change": 1, "fix": 2, "repo": 3}[.] // 99)) |

reduce .[] as $group (
  "";
  reduce $group[] as $commit (
    . + "## `\($group.[0].simple_type)`\n\n";
    . +
    "- \($commit.subject). (\($commit.commit)) @\($commit.author.github) (`index: \($commit.index)`)" +
    if $commit.isSkipCI then " (_Skip CI_)" else "" end +

    if $commit.body != null and $commit.body != "" then
      "\n\n  \($commit.body | gsub("\n"; "\n  "))"
    else
      ""
    end +
    "\n\n"
  )
)
