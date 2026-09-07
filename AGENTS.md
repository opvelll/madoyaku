# Repository instructions

- UIに影響する変更後は `.\scripts\Verify-Ui.ps1` を実行する。
- 出力された `report.md` の全PNGを確認し、文字・配色・切れ・配置・操作状態を確認する。
- 画像確認と自動確認が成功したら、表示された `Complete-Verification.ps1` コマンドを実行する。
- 実APIキーと実API通信は確認に使わない。確認不能時は理由と未確認範囲を報告する。
- UIに影響しない変更でも、Releaseビルド、format検証、`git diff --check` は必須。
