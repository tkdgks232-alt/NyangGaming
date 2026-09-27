# 냥게이밍 업데이트 배포 안내

## 현재 완료 상태

Velopack 1.2.158 기반 설치·업데이트 기능과 GitHub Actions workflow를 구현했습니다.
로컬 테스트 설치본에서 1.0.0 → 1.0.1 감지/대화상자/다운로드/검증/종료/교체/재실행을 확인했습니다.
GitHub 저장소: https://github.com/tkdgks232-alt/NyangGaming . 첫 Release의 Actions 빌드·테스트·공개와 인증 없는 피드 조회·패키지 다운로드 검증을 완료했습니다.
GitHub 배포처가 설정되지 않은 빌드는 설정에 그 상태를 표시하며 임의 URL에 요청하지 않습니다.

## 처음 한 번

1. GitHub에서 Public 빈 저장소를 만듭니다. README 자동 생성은 끄면 편합니다.
2. 준비한 소스 폴더를 GitHub Desktop으로 가져와 해당 저장소에 게시하거나 Git으로 push합니다.
3. version.json의 githubRepository를 https://github.com/본인계정/NyangGaming 형태로 설정합니다.
4. 첫 버전 1.0.0 소스를 commit/push하고 v1.0.0 tag를 push합니다.
5. Actions가 끝나면 Releases에서 NyangGaming-win-Setup.exe를 받습니다.
6. 실행 중인 기존 ZIP 앱은 트레이에서 완전히 종료한 뒤 Setup을 한 번 실행합니다. 기존 앱 제거와 데이터 삭제는 하지 않습니다.
7. 이후에는 설치된 Nyang Gaming 바로가기로 실행합니다. ZIP의 옛 실행 파일은 새 설치로 자동 전환되지 않습니다.

초기 Git 명령(전용 소스 폴더에서, GitHub 로그인은 Git Credential Manager 등 정상 로그인 사용):

```powershell
git init -b main
git add .
git commit -m "Add Nyang Gaming updater"
git remote add origin https://github.com/본인계정/NyangGaming.git
git tag -a v1.0.0 -m "Nyang Gaming v1.0.0"
git push --atomic -u origin main refs/tags/v1.0.0
```

GitHub Actions의 기본 GITHUB_TOKEN을 배포 단계에만 사용합니다. 앱에는 GitHub 토큰이 들어가지 않습니다.
저장소는 공개여야 합니다. 자동 업데이트 클라이언트가 로그인 없이 공개 Release 자산을 받습니다.
소스를 공개하지 않으려면 별도 비공개 소스 저장소와 공개 배포 저장소로 workflow 권한 구성을 변경해야 합니다.

## 다음 버전 배포

코드 변경을 commit한 다음 변경사항을 설명하는 메모 파일을 준비합니다.

```powershell
.\Release.ps1 -Version 1.0.1 -NotesFile C:\경로\변경사항.txt
```

스크립트가 버전/릴리스 노트 commit, tag, push를 수행합니다. 이후 GitHub Actions를 확인하면 됩니다.
이미 있는 버전/태그를 덮어쓰지 않습니다. 실패 시 버전 번호를 임의로 되돌리거나 force-push하지 않습니다.
Workflow는 모든 자산을 draft Release에 먼저 올리고 성공하면 공개합니다.
PC가 꺼져 있어도 게시된 Release 다운로드는 GitHub에서 제공됩니다.

## 로컬 빌드/패키징

개발 도구: Windows 10/11, .NET Framework 4.8, Node 22 + npm, .NET SDK 8, Git.
사용자 PC에는 SDK/Node/npm/Git이 필요하지 않습니다. Setup은 net48/WebView2 런타임을 확인합니다.

```powershell
.\Restore.ps1
dotnet tool install vpk --version 1.2.158 --tool-path .tools
.\Package.ps1 -Vpk "$pwd\.tools\vpk.exe"
```

Package.ps1은 배포 저장소가 비어 있으면 정식 설치기를 만들지 않습니다.
Releases 폴더의 Setup.exe, full.nupkg, releases.win.json 및 보조 메타데이터를 같은 Release에 업로드해야 합니다.
현재는 안정성을 위해 full 패키지 단위 업데이트를 사용하며 delta 패키지는 생성하지 않습니다.
코드 서명 인증서는 연결되어 있지 않습니다. 필요하면 Velopack 서명 옵션과 Actions Secrets를 추가할 수 있습니다.

## 데이터와 실패 처리

- 새 설치 경로: %LOCALAPPDATA%\NyangGaming\current (Velopack 기본값)
- 기존 사용자 데이터: %LOCALAPPDATA%\KoruGaming_Next\UserData (변경하지 않음)
- 설정/settings.json, Artwork, HoYoLAB 세션, 게임 연결, 친구 세션/설정, 전적 캐시, 채팅 설정 모두 사용자 데이터 폴더 유지
- 친구 관계/대화/아바타 등 서버 데이터는 기존 Supabase 프로젝트를 그대로 사용
- 랭크 SQLite: UserData\league-ranks.sqlite. Windows 기본 winsqlite3.dll 사용
- WebView2 사용자 프로필: UserData\WebView2, 로그인 프로필도 사용자 데이터 경로 사용
- SQLite migration은 백업 API로 .bak 생성 후 트랜잭션 실행. 실패 시 ROLLBACK; DB 삭제/초기화 금지
- 다운로드 실패/해시 검증 실패는 적용하지 않음. 앱을 계속 사용하며 재시도 가능
- 재시작 버튼 선택 전에 다운로드만으로 자동 적용하지 않음. 다음 실행 시 자동 적용도 끔
- 업데이트 실행 전 설정 저장. 정상 종료 후 Velopack이 프로그램 패키지만 교체
- 기존 자동 실행 옵션이 켜져 있었다면 설치형 실행 경로로 갱신
- frontend와 host는 같은 Version으로 빌드하며 시작 시 버전 일치 검증

## 검증 범위와 한계

- 실제 별도 설치본에서 1.0.0 → 1.0.1 완료, 자동 재실행 및 Host/React 1.0.1 확인
- 손상된 실제 패키지 다운로드를 거부하고 1.0.0 실행 상태 유지 확인
- 합성 테스트 데이터로 테마/플레이시간/개인 배경/프로필/친구/채팅/게임 연결/세션 파일 보존 및 SQLite 레코드 보존 확인
- 업데이트 정책/오류/SQLite rollback 테스트 16개 통과
- 업데이트 UI 두 테마/두 창 폭/확대 100·150%와 닫기 총 60개 검증 통과
- 기존 채팅 화면 검증 포함. 실제 친구에게 테스트 메시지를 보내지 않음
- 실제 GitHub Actions 빌드·업로드·공개 완료, Velopack GithubSource로 공개 피드/노트/패키지 다운로드 검증 완료
- 임의 전원 차단 등 모든 OS 장애를 재현한 것은 아님. 업데이트 적용은 Velopack의 파일 교체/복구 흐름 사용

참고: https://docs.velopack.io/integrating/overview
https://docs.velopack.io/packaging/operating-systems/windows
https://docs.velopack.io/distributing/github-actions
