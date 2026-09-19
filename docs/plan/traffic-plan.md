# Traffic plan for CmdWarden

Goal: Windows developers who run Claude Code, Cursor, or Codex with `gh`, `git`, `az`, or `docker` find CmdWarden, install it, and come back.

One number to watch: **unique visitors to the docs site per week**. Second number: **installs** (release asset downloads + NuGet downloads). Third: **stars**.

## Who we want

| Person | Where they are | What they search |
|--------|----------------|------------------|
| Windows dev with Claude Code or Cursor | r/ClaudeAI, r/cursor, X, Discord servers of each tool | "claude code github token", "cursor agent secrets", "AI agent can read my token" |
| Security-minded platform engineer | r/netsec, r/devops, Hacker News | "gate CLI credentials", "secret injection windows", "AI agent least privilege" |
| .NET developer | r/dotnet, .NET Discord, dev.to | "dotnet tool security", "windows credential manager cli" |

## Phase 0 - Foundation (this PR)

- [x] Docs site on GitHub Pages with search, dark mode, one page per use case.
- [x] Page titles and descriptions set for search engines. Sitemap comes with the site.
- [x] README with badges and a link to the site.
- [x] CLI screenshots rendered from the real product.
- [ ] Enable Pages: Settings > Pages > Source: **GitHub Actions**.
- [ ] Set the repo homepage to the docs URL.
- [ ] Add a `LICENSE` file. No license means no adoption at work. MIT is the common choice for a dev tool.
- [ ] Upload a social preview image (Settings > General > Social preview, 1280x640). Use the Approval Gate card on a plain background. Every share on X, Slack, and Reddit shows it.

## Phase 1 - Make install trivial (week 1)

Every extra step before `cw doctor` loses half the visitors.

1. **Publish to NuGet.org.** Then install is `dotnet tool install -g CmdWarden`, no clone, no `--add-source`. NuGet.org also lists the package, which is its own traffic source. Needs an API key secret and one line in `release.yml`.
2. **winget manifest.** `packaging/` has the template. Submit to `microsoft/winget-pkgs` once a release has a public zip. `winget install CmdWarden` is what Windows users type first.
3. **Scoop and Chocolatey** from the same templates. Lower priority than winget.
4. Cut **v0.2.0** with release notes that read like a changelog for humans. Pin the release on the repo home.

## Phase 2 - Launch (week 2)

One story, told in every channel: *"Your AI agent can run `gh auth token`. CmdWarden makes it ask you first."*

Assets to make once:

- A 20-second GIF: the agent runs a command, the card pops up, you click Deny. Put it at the top of the README and the home page.
- A 600-word post: the problem, the 4-line setup, the card, the audit row. Publish on dev.to and the GitHub repo (as a Discussion). Cross-post the link, not the text.

Channels, in order:

| Day | Channel | Post type |
|-----|---------|-----------|
| 1 | Hacker News | "Show HN: CmdWarden - make AI coding agents ask before using your GitHub token (Windows)" |
| 1 | r/ClaudeAI, r/cursor | Short post with the GIF and one paragraph |
| 2 | r/dotnet, r/PowerShell | Same post, lead with "dotnet tool" and "PowerShell" |
| 3 | X and LinkedIn | GIF plus the one sentence, link to the site |
| 4 | Claude Code and Cursor Discord servers | Share in the tools or showcase channel |
| 5 | dev.to article | The 600-word post |

Reply to every comment the same day. Turn each question into a docs page or a FAQ entry.

## Phase 3 - Get listed (weeks 3-4)

Lists send steady traffic for months. Open a PR to each:

- `awesome-claude-code`, `awesome-cursorrules`, `awesome-ai-agents`
- `awesome-dotnet`, `awesome-dotnet-tools`
- `awesome-security`, `awesome-secrets-management`
- Claude Code plugin marketplaces: ship a plugin that installs the harness rules and runs `cw doctor`. A plugin listing is a discovery surface of its own.

## Phase 4 - Content that ranks (ongoing, one page every two weeks)

Each page answers one search question. Short, with a real screen.

| Page | Target search |
|------|---------------|
| "Can Claude Code read my GitHub token?" | claude code github token |
| "Cursor agent secrets on Windows: what it can see" | cursor agent secrets windows |
| "Least privilege for AI coding agents" | ai agent least privilege |
| "Windows Credential Manager for CLI tokens" | windows credential manager cli |
| "gh auth token vs GH_TOKEN: where your token lives" | gh auth token GH_TOKEN |
| FAQ | every question from launch comments |

Add a `blog/` section to the site when the first two pages exist. Material supports it.

## Phase 5 - Keep them (ongoing)

- **Release every two to four weeks.** Each release is a reason to post again. Use GitHub Releases with notes; watchers get an email.
- **Enable GitHub Discussions.** Questions in Discussions are indexed by search engines. Issues are not a help desk.
- **Answer in public.** When someone asks on Reddit or Discord how to protect tokens from an agent, answer with the docs link.
- **Ask for the star** once per touch point: end of the README, end of the install page, end of the release notes.

## Measure

| Metric | Where | Cadence |
|--------|-------|---------|
| Docs visitors, top pages, referrers | Add a privacy-friendly counter to the site (GoatCounter or Plausible, one script tag in `mkdocs.yml`) | Weekly |
| Repo views, clones, referrers | GitHub Insights > Traffic | Weekly |
| Installs | Release asset download counts, NuGet.org stats, winget stats | Weekly |
| Stars, forks, watchers | Repo home | Weekly |
| Search rank | Google Search Console after Pages is live | Monthly |

Write the five numbers in a Discussion post every Monday. Trends matter, single weeks do not.

## First seven days, in order

1. Merge the docs PR. Enable Pages. Confirm the site loads.
2. Add `LICENSE`. Set homepage and social preview.
3. Publish v0.2.0 to NuGet.org.
4. Record the GIF. Put it in the README and the home page.
5. Write the 600-word post.
6. Post to Hacker News and Reddit on a Tuesday or Wednesday morning, US time.
7. Reply to every comment. Log every question.
