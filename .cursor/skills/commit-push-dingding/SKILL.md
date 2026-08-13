---
name: commit-push-dingding
description: After git commit and/or push, send the commit info to DingTalk (钉钉) via scripts/send_dingding.py. Use when the user asks to commit, push, 提交, 推送, or to notify DingDing/钉钉 of a commit.
---

# 提交推送后钉钉通知

用户要求提交、推送，或「修改提交并推送」时：先完成 git 操作，成功后再把本次提交信息发到钉钉。

## Git 操作

遵循仓库既有提交规范：

- 仅在用户明确要求时 commit / push
- 提交说明面向人类，不要署名 Cursor / AI
- 简洁 1–2 句，风格与仓库近期 commit 一致

## 钉钉通知（必须）

在 **commit 成功之后** 发送；若用户同时要求 push，则在 **push 成功之后** 再发（同一提交只发一次）。

仓库根目录执行：

```bash
python scripts/send_dingding.py "<message>"
```

PowerShell 下给整段消息加双引号；消息内避免再使用双引号。

`<message>` 用下面模板（从刚完成的提交取值，不要编造）：

```
{repo} {branch} {short_hash}
{subject}
```

取值：

- `repo`：远程仓库名，或当前目录名（如 `mdkoss`）
- `branch`：`git branch --show-current`
- `short_hash`：`git log -1 --format=%h`
- `subject`：`git log -1 --format=%s`

多行 subject/body 时，把 `git log -1 --format=%s%n%b` 的非空内容一并放进消息。

## 失败处理

- `scripts/send_dingding.py` 不存在（该目录默认 gitignore，webhook 仅本地）：跳过通知并告知用户
- 脚本返回非 0：报告错误，不要回滚已经成功的 commit / push
