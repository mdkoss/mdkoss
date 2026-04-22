# Contributing to MDKOSS / MDKOSS 贡献指南

Thanks for helping improve MDKOSS.  
感谢你帮助改进 MDKOSS。

## Ground Rules / 基本原则

- Be respectful and constructive in all discussions.  
  在所有讨论中保持尊重与建设性。
- Keep changes focused and easy to review.  
  变更应聚焦单一目标，便于评审。
- Prefer small pull requests with clear intent.  
  优先提交意图清晰的小型 PR。

## Development Setup / 开发环境

1. Install the .NET SDK (recommended: latest LTS).  
   安装 .NET SDK（建议最新 LTS 版本）。
2. Clone the repository.  
   克隆仓库代码。
3. Run locally / 本地运行：

```bash
dotnet run --project MDKOSS.csproj
```

## Contribution Workflow / 贡献流程

1. Fork the repository and create a feature branch.  
   Fork 仓库并创建功能分支。
2. Make your changes and include tests when applicable.  
   完成修改，并在适用时补充测试。
3. Ensure the project builds successfully.  
   确认项目可以成功构建。
4. Open a pull request with / 提交 PR 时请包含：
   - Change summary / 变更摘要
   - Motivation and impact / 变更动机与影响范围
   - Verification steps / 验证步骤

## Coding Expectations / 代码要求

- Follow existing project structure and naming style.  
  遵循现有项目结构与命名风格。
- Avoid unrelated refactoring in the same PR.  
  避免在同一 PR 中夹带无关重构。
- Add concise comments only where logic is not obvious.  
  仅在逻辑不直观处补充简洁注释。

## Issues and Feature Requests / 问题与功能建议

- Use GitHub Issues for bugs and proposals.  
  通过 GitHub Issues 提交缺陷和建议。
- Include reproduction steps, expected behavior, and actual behavior for bugs.  
  缺陷报告需包含复现步骤、期望行为与实际行为。

By contributing, you agree your contributions are licensed under the MIT License.  
提交贡献即表示你同意你的贡献内容按 MIT License 授权。
