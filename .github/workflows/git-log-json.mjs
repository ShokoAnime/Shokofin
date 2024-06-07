#! /bin/env node
import { execSync } from "child_process";
import process from "process";

// https://git-scm.com/docs/pretty-formats/2.21.0

// Get the range or hash from the command line arguments
const RangeOrHash = process.argv[2] || "";

// Form the git log command
const GitLogCommandBase = `git log ${RangeOrHash}`;

const Placeholders = {
    "H": "commit",
    "P": "parents",
    "T": "tree",
    "s": "subject",
    "b": "body",
    "an": "author_name",
    "ae": "author_email",
    "aI": "author_date",
    "cn": "committer_name",
    "ce": "committer_email",
    "cI": "committer_date",
};

const commitOrder = [];
const commits = {};

for (const [placeholder, name] of Object.entries(Placeholders)) {
    const gitCommand = `${GitLogCommandBase} --format="%H2>>>>> %${placeholder}"`;
    const output = execSync(gitCommand).toString();
    const lines = output.split(/\r\n|\r|\n/g);
    let commitId = "";

    for (const line of lines) {
        const match = line.match(/^([0-9a-f]{40})2>>>>> /);
        if (match) {
            commitId = match[1];
            if (!commits[commitId]) {
                commitOrder.push(commitId);
                commits[commitId] = {};
            }
            // Handle multiple parent hashes
            if (name === "parents") {
                commits[commitId][name] = line.substring(match[0].length).trim().split(" ");
            }
            else {
                commits[commitId][name] = line.substring(match[0].length).trimEnd();
            }
        }
        else if (commitId) {
            if (name === "parents") {
                const commits = line.trim().split(" ").filter(l => l);
                if (commits.length)
                    commits[commitId][name].push(...commits);
            }
            else {
                commits[commitId][name] += "\n" + line.trimEnd();
            }
        }
    }
}

// Trim trailing newlines from all values in the commits object
for (const commit of Object.values(commits)) {
    for (const key in commit) {
        if (typeof commit[key] === "string") {
            commit[key] = commit[key].trimEnd();
        }
    }
}

// Convert commits object to a list of values
const commitsList = commitOrder.slice().reverse()
    .map((commitId) => commits[commitId])
    .map(({ commit, parents, tree, subject, body, author_name, author_email, author_date, committer_name, committer_email, committer_date }) => ({
        commit,
        parents,
        tree,
        subject: /^\w+: /i.test(subject) ? subject.split(":").slice(1).join(":").trim() : subject.trim(),
        type: /^\w+: /i.test(subject) ?
                subject.split(":")[0].toLowerCase()
            : subject.startsWith("Partially revert ") ?
                "revert"
            : parents.length > 1 ?
                "merge"
            : /^fix/i.test(subject) ?
                "fix"
            : "misc",
        body,
        author: {
            name: author_name,
            email: author_email,
            date: new Date(author_date).toISOString(),
            timeZone: author_date.substring(19) === "Z" ? "+00:00" : author_date.substring(19),
        },
        committer: {
            name: committer_name,
            email: committer_email,
            date: new Date(committer_date).toISOString(),
            timeZone: committer_date.substring(19) === "Z" ? "+00:00" : committer_date.substring(19),
        },
    }))
    .map((commit) => ({
        ...commit,
        subject: /[a-z]/.test(commit.subject[0]) ? commit.subject[0].toUpperCase() + commit.subject.slice(1) : commit.subject,
        type: commit.type == "feature" ? "feat" : commit.type === "refacor" ? "refactor" : commit.type == "mics" ? "misc" : commit.type,
    }))
    .filter((commit) => !(commit.type === "misc" && (commit.subject === "update unstable manifest" || commit.subject === "Update repo manifest" || commit.subject === "Update unstable repo manifest")));

process.stdout.write(JSON.stringify(commitsList, null, 2));
