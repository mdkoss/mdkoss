---
name: commit-push-dingding
description: Commit related files, push when asked, notify DingTalk once, and after a successful push insert a resolved MySQL issue. Use when the user asks to commit, push, 提交, 推送, or 提交并推送.
---

# 提交 / 推送流程

用户说「提交」「推送」「提交并推送」时按本流程做。只做用户点名的步骤：只说提交就不要 push；只说推送就不要新开 commit。push 成功后必须写 MySQL issue（见第 4 节）。

## 1. 提交（用户明确要求时）

并行收集：

```powershell
git status
git diff
git log -12 --oneline
```

已暂存的再看 `git diff --cached`。PowerShell 不要用 `&&`，用 `;` 或分开执行。不要用 `git add -i` / `git rebase -i`。

然后：

1. 只 `git add` 本次相关文件。不要加无关改动、密钥、`.gitignore` 本地目录、`scripts/`（含密码）、`android/`（除非用户点名）。
2. 看近期 commit 风格，写 1–2 句中文说明，侧重 why。常用前缀：`feat:` / `fix:` / `chore:`。
3. **不要**在 message / trailer 里写 Cursor、CursorAgent、AI、`Co-authored-by: Cursor`。
4. **不要**改 git config、**不要** `--no-verify`、**不要** amend（除非用户明确要求且符合仓库 amend 条件）。
5. PowerShell 用 here-string 传说明：

```powershell
git add <相关文件>
git commit -m @"
chore: 一句话说明原因

可选第二句补充。
"@
git status
```

空变更不要空提交。hook 改了文件且 commit 失败时，修好后**新开**一次 commit，不要 amend。

## 2. 推送（用户明确要求时）

```powershell
git push origin HEAD
```

- 不要 force push `main` / `master`。用户要求 force 时先警告。
- 不要 `git push --force`，除非用户一字不差要求。
- PowerShell 里 `@{u}` 会被当成 hashtable，需要跟踪信息时写成 `'@{u}'`。

## 3. 钉钉（必须，同一提交只发一次）

- 只 commit：commit 成功后发；不要写 MySQL issue。
- 同一次对话里「提交并推送」：等 **push 成功后再发钉钉**，再写 issue。
- 之前已经为这个 hash 发过、用户后来又说推送：push 后**不要重发钉钉**，也不要重复插 issue。

仓库根目录：

```powershell
python scripts/send_dingding.py "mdkoss master 66f4f1b
chore: 提交说明第一行
可选 body 第二行。"
```

取值（不要编造）：

- `repo`：远程名或目录名（`mdkoss`）
- `branch`：`git branch --show-current`
- `short_hash`：`git log -1 --format=%h`
- 正文：`git log -1 --format=%s`；有 body 则一并放入，**去掉** `Co-authored-by:` 等 trailer

PowerShell 整段消息用双引号；消息内不要再套双引号。

`scripts/send_dingding.py` 不存在（目录常被 gitignore）：跳过并告知用户。脚本非 0：报告错误，**不要回滚**已成功的 commit / push。

## 4. 已完成 Issue（push 成功后必须）

**push 成功后**向公网 MySQL `mdkossdb` 插入一条 `resolved` issue，对应刚推上去的那次提交。只 commit 不 push 时不要写 issue。同一 hash 不要重复插入。

连接用 `scripts/mdkossdb/test_conn.py` 的 `CONFIG`，不要把密码写进仓库文件。标题用本次 commit 第一行（可略改成 issue 语气），body/评论写改了什么、为什么、怎么验证，并带上 `branch` 与 `short_hash`。

```python
import sys
from pathlib import Path
import pymysql
sys.path.insert(0, str(Path("scripts/mdkossdb").resolve()))
from test_conn import CONFIG

conn = pymysql.connect(**CONFIG)
with conn:
    with conn.cursor() as cur:
        cur.execute(
            """INSERT INTO issues
               (title, body, type, priority, status, reporter, assignee, module, closed_at)
               VALUES (%s, %s, %s, %s, 'resolved', %s, %s, %s, NOW())""",
            (title, body, "feature", "medium", "K", "K", "other"),
        )
        issue_id = cur.lastrowid
        cur.execute(
            "INSERT INTO issue_comments (issue_id, author, body) VALUES (%s, %s, %s)",
            (issue_id, "K", comment),
        )
    conn.commit()
```

字段约定：

| 列 | 取值 |
|----|------|
| `type` | `bug` / `feature` / `question` / `other` |
| `priority` | `low` / `medium` / `high` / `critical` |
| `status` | 完成用 `resolved`，并写 `closed_at = NOW()` |
| `module` | `axis` / `gpio` / `vision` / `recipe` / `driver` / `other` |
| `title` | ≤200 字，对应刚做完的事 |
| `body` / 评论 | 改了什么、为什么、怎么验证；不要 markdown 标题，不要双引号 |

插完后回报 `id`、标题、`status`。不要新建 `scripts/` 下的提交用脚本。

## 失败处理

- commit / push 失败：停在当前步，说明原因。
- 钉钉或 Issue 失败：git 结果保留，只报告外部步骤失败。
