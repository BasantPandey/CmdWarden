# LinkedIn post: CmdWarden launch

Launch draft. LinkedIn shows the first two lines, then "...more". The hook must land in line one.

## Post

Your AI coding agent can read your GitHub token. On Windows, nothing stops it.

I found this out the hard way. Claude Code ran `gh auth token` in my terminal. The token came back in plain text. Cursor and Codex can do the same. Any process the agent starts gets the same credentials my terminal has.

So I built CmdWarden.

It sits between the launcher and the tool. Your terminal stays trusted. The AI harness gets a policy. When the agent runs gh, git, az, or docker, a small card pops up on the desktop:

Deny. Allow for session. Approve Once.

What makes it different:

- The secret goes into one child process for one run. The agent's shell never holds it.
- Policy is per tool and per launcher. Read for the agent, Full for you.
- Every decision writes an audit row first. If the card cannot show, the request fails closed.
- Setup is four commands. Day to day you still type `gh` and `git` as usual.

It is open source, MIT, written in C# on .NET 10, for Windows 10 and 11.

Docs with nine short use cases, from "gate my GitHub token" to "undo everything":
https://basantpandey.github.io/CmdWarden/

Code and issues:
https://github.com/BasantPandey/CmdWarden

If you run AI coding agents on Windows, try it and tell me what breaks. If it saves you a token, a star helps the next person find it.

#AIAgents #DevSecOps #Windows #dotnet #ClaudeCode #Cursor #GitHub #OpenSource

## Image

Attach `docs/images/approval-gate.png`. One image. A carousel gets fewer views than one strong image.

## Comment to add right after posting

Post this as the first comment. LinkedIn shows posts with links in the body to fewer people. The post above keeps the links, so this comment repeats the docs link for people who scroll.

> Setup in four lines:
> cw doctor
> cw policy enroll --kind terminal
> cw policy enroll --kind ai-harness
> cw harden gh
>
> Full guide: https://basantpandey.github.io/CmdWarden/

## Short version

For a repost a week later, or for a comment on someone else's post about agent security.

> AI coding agents on Windows can read the same tokens your terminal has. CmdWarden makes them ask you first: Deny, Allow for session, or Approve Once. Open source, MIT. https://basantpandey.github.io/CmdWarden/

## Rules that raise the reach

- Post Tuesday to Thursday, 8 to 10 AM your local time.
- Reply to every comment within the first hour. Replies push the post to more feeds.
- Do not edit the post in the first hour. An edit resets the reach.
- Ask three people you know to comment, not only to like. A comment counts more.
- Tag no company in the post. Tag a person in a comment if they know the space.
- Repost your own post after seven days with one new line on top. Example: "Most asked question: does it work with Cursor? Yes."
- Set the profile headline to include "Building CmdWarden". Add the docs link to the profile.

## Checklist

- [ ] Post the text above with `approval-gate.png`.
- [ ] Add the first comment with the four setup lines.
- [ ] Reply to every comment for the first two hours.
- [ ] Repost after seven days.
