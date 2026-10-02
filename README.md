# Nyang Gaming · 냥게이밍

게임 활동과 전적, 친구 상태와 채팅을 한곳에서 확인하는 Windows 앱입니다.

## 설치

[최신 Setup 다운로드](https://github.com/tkdgks232-alt/NyangGaming/releases/latest/download/NyangGaming-win-Setup.exe)

1. 기존 ZIP 버전이 실행 중이면 트레이에서 **완전히 종료**합니다.
2. Setup을 최초 한 번 실행합니다. 기존 앱 제거나 사용자 데이터 삭제는 필요하지 않습니다.
3. 이후 설치된 Nyang Gaming 바로가기로 실행합니다. 새 버전은 앱 안에서 확인·다운로드·재시작하여 적용할 수 있습니다.

기존 설정·연결·개인 배경·플레이시간은 `%LOCALAPPDATA%\KoruGaming_Next\UserData`에 유지됩니다.

## 새 버전 배포

v1.0.2에는 친구별 접속 알림, 수동 파티 상태, 내 주간 플레이 요약이 포함됩니다. 친구 탭에서 설정하고 확인할 수 있습니다. 주간 요약은 한국 시간 월요일부터 현재까지의 완료된 게임 기록과 지난주 전체를 비교합니다. 기록 공유 설정과 관계없이 본인만 조회하는 요약입니다.

서버 관리자는 v1.0.2 배포 전에 기존 Supabase SQL Editor에서 [backend/social.sql](backend/social.sql)을 실행해야 합니다. `nyang_social_ready` 결과를 확인하세요. 기존 테이블이나 함수는 삭제하지 않으며, 구버전 친구·채팅 기능을 유지합니다. 서버 설정이 누락되어도 기존 기능은 계속 동작하고 파티·주간 요약에만 연결 실패가 표시됩니다.

변경 내용은 설치 파일에 함께 포함되며, 새 버전의 첫 실행 때 한 번 표시됩니다. **설정 → 이번 버전 변경 내용**에서 다시 확인할 수 있습니다.

코드 변경을 커밋한 뒤 변경사항 메모 파일을 준비하고 실행합니다.

```powershell
.\Release.ps1 -Version 1.0.3 -NotesFile C:\경로\변경사항.txt
```

[Actions](https://github.com/tkdgks232-alt/NyangGaming/actions)에서 빌드 결과를 확인하세요. 성공하면 [Releases](https://github.com/tkdgks232-alt/NyangGaming/releases)에 자동 게시됩니다.
자세한 절차와 데이터 보존 구조는 [배포 안내](UPDATE-GUIDE.md)에 있습니다.

게임사 및 전적 사이트와 별개인 개인 프로젝트입니다.
