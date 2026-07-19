# Gap-Analyse: „Return of King"-Server-Build → OpenKO .NET-Port

**Quelle:** kompilierte „official"-Binaries (`Ebenezer`, `AI Server`, `Aujard`; PE32/x86)
unter `dotnet/_reversing/`, per Reverse Engineering analysiert (`strings`, IDA-Artefakte,
Ghidra-Headless). Eingebettete Quellpfade: `\server\RetrunOfKing\ebenezer\...` →
identifiziert als der spätere kommerzielle **„Return of King" (RoK)**-Build, gleiche
**1298/9**-Protokoll-Ära wie der vorhandene OpenKO-C++-Code, aber mit vielen
Zusatz-Subsystemen (überwiegend China/Korea-PC-Bang-Features).

**Baseline für den Diff:** der vorhandene C++-Quellcode (`Server/`, `shared/`) und der
`dotnet/`-Port, der diesen 1:1 nachbildet.

## Konfidenz-Konvention (reverse_engineer-Skill)

- `VERIFIED` — aus der Binary belegt (String/Disassembly) bzw. gegen die echte DB/Client getestet.
- `INFERRED` — aus Strings/Verhalten rekonstruiert, interne Logik unbestätigt.
- Im Code werden offene Stellen mit `// TODO(re): …` / `// GUESS: …` markiert.

**Harte Grenze:** Es liegen nur die Server-Binaries vor — **kein** passender Client, **kein**
RoK-DB-Schema (Stored-Proc-**Bodies** leben in der DB, nicht in der Exe), keine Captures.
Damit ist **keine** byte-genaue End-to-End-Parität wie bei den quell-generierten
Golden-Vectors möglich. Paket-Layouts neuer Handler werden per Disassembly als `INFERRED`
abgeleitet, bis Client/Capture sie bestätigen.

## Kernbefund: 55 „schon in der DB" vs. 37 „echt neu"

Diff der **92** von den Exes aufgerufenen Stored-Procs gegen `deps/db-models/StoredProc/`
(die C++-ORM-Binder ⇒ Indikator, ob die Proc im OpenKO-db-Schema existiert):

### A) 55 Procs mit vorhandenem Binder ⇒ liegen sehr wahrscheinlich schon in der DB
→ **Reine Verdrahtungs-Lücken**: Proc existiert, aber C++/Port ruft sie nicht auf / hat
keinen Paket-Handler. **Gegen die echte DB verifizierbar (`OPENKO_TEST_DB`).** Priorität B1.

Subsystem-Cluster:
- **King-/Election-/Impeachment-System** (12): `KING_ELECTION_PROC`, `KING_IMPEACHMENT_ELECTION`,
  `KING_IMPEACHMENT_REQUEST_ELECTION`, `KING_IMPEACHMENT_RESULT`, `KING_CHANGE_TAX`,
  `KING_CANDIDACY_NOTICE_BOARD_PROC`, `KING_CANDIDACY_RECOMMEND`, `KING_INSERT_PRIZE_EVENT`,
  `KING_UPDATE_ELECTION_LIST/SCHDULE/STATUS`, `KING_UPDATE_IMPEACHMENT_STATUS`,
  `KING_UPDATE_NOAH_OR_EXP_EVENT`. (Ebenezer: `KingSystem.cpp`)
- **Rental-Item-System** (6): `RENTAL_ITEM_LEND/REGISTER/CANCEL/DESTORY/DURABILITY_UPDATE`,
  `LOAD_RENTAL_DATA`. (`RentalSystem.cpp`, `RentalManager.cpp`; GM: `/rental_start|_stop|_report`)
- **Saved-Magic** (Buffs über Relog): `LOAD_SAVED_MAGIC`, `UPDATE_SAVED_MAGIC`.
- **Skill-Shortcut** (Hotkey-Leiste speichern): `SKILLSHORTCUT_LOAD`, `SKILLSHORTCUT_SAVE`.
- **Knights/Siege-Erweiterungen**: `UPDATE_KNIGHTS_ALLIANCE/MARK/WAR`, `CHANGE_KNIGHTS_CAPE`,
  `UPDATE_SIEGE`, `UPDATE_SIEGE_CHALLENGER2`, `UPDATE_SIEGE_DECIDE_CHALLENGER`.
  (`KnightsAllianceSet.cpp`, `KnightsCapeSet.cpp`, `KnightsSiegeWarSet.cpp`)
- **Friend-List** (Port-Base = `WIZ_FRIEND_PROCESS` `#if 0` no-op): `INSERT_FRIEND_LIST`,
  `DELETE_FRIEND_LIST`.
- **Premium-Service / Web-ItemMall**: `LOAD/UPDATE_PREMIUM_SERVICE_USER`, `LOAD_WEB_ITEMMALL`.
- **Sonstiges**: `CHANGE_CASTLE_COMMERCE`, `CLEAR_REMAIN_USERS`, `RESET_LOYALTY_MONTHLY`,
  `UPDATE_BATTLE_HERO`, `UPDATE_BATTLE_RESULT`, `LOAD_CHAR_INFO`, `PROC_INSERT_CURRENTUSER`,
  `INSERT_HACKTOOL_USER`, `INSERT_PROGRAM_CHECK_USER`.

### B) 37 Procs ohne Binder ⇒ echt neu (China/Kommerz-Cluster)
→ Proc-**Bodies unbekannt** ohne RoK-Schema, `INFERRED`, niedrigere Konfidenz. Priorität B2.

- **PP-Card-Trade (China)**: `INQUIRY_PPCARDTRADE_RESULT(_AFTER)`, `PPCARD_TRADE_DRAWOUT`,
  `INSERTPPCARDSELLLIST`, `PPCARDTRADERESULTRECORD`, `CHECK/UPDATE_PPCARD_EVENT`,
  `INQUIRY_TRADE_MONEY/SELLMONEY`. (`PPCardTradeSystemChina.cpp`, `PPCardTradeManagerChina.cpp`)
- **GameBang / PC-Bang**: `CHECK_GAMEBANG_IP/USER`, `GET_GAMEBANG_DATA/EVENT_ITEM/LOTTERY_ITEM`,
  `UPDATE_GAMEBANG_LEVEL_EVENT`.
- **ShoppingMall**: `SHOPPINGMALL_BUY`, `POWERUP_SHOPPINGMALL_WRIGHTLOG`. (`ShoppingMall.cpp`)
- **Olympic / KJWar / LogTime**: `CHECK_OLYMPIC_ACCOUNT`, `OLYMPIC_ITEM_LOG`,
  `CHECK_KJWAR_ACCOUNT`, `GET_KJWAR_DATA`, `CHECK_LOGTIME_ACCOUNT`, `GET_LOGTIME_DATA`.
- **Account-Admin**: `ACCOUNT_FORBID`, `CREATE_NEWACCOUNT`, `CHANGE_NEW_ID` (Namensänderung),
  `LOAD_ACCOUNT_AUTHORITY`, `UPDATE_WAREHOUSE_PW` (Lager-Passwort), `DELETE_CHAR`.
- **Events/Reporting**: `CHECK/UPDATE_COUPON_EVENT`, `UPDATE_EMIGRATION_EVENT`,
  `MAKE_HOUR_REPORT_NOAH`, `MAKE_MONSTER_ITEM_REPROT`, `MAKE_MONSTER_REPROT_DAILY`,
  `INSERT_BATTLE_STATISTICS_CHINA`, `PROC_UPDATE_CURRENTUSER`.

## Weitere RoK-Subsysteme (aus eingebetteten Quelldateinamen)

Neue `ebenezer\*.cpp` gegenüber der OpenKO-Base: `KingSystem`, `PetSystem`,
`MonsterChallengeSet(+SummonList)`, `Rental{Item,Manager,System}`,
`PPCard{ItemSet,TradeManagerChina,TradeSCListSet,TradeSystemChina}`, `ShoppingMall`,
`CouponSerialListSet`, `Item{Upgrade,UpProbability}Set`, `HomeSet`, `MerchantMode`,
`Knights{Alliance,Cape,Rank,SiegeWar}Set`, `User{Personal,}RankSet`, `WebPageAddressSet`,
`CoefficientSet`, `ServerResourceSet`, `EventTriggerSet`.

## Neue DB-Tabellen (referenziert)

`MONSTER_CHALLENGE(+_SUMMON_LIST)`, `RENTAL_ITEM`, `PPCARD_ITEM_PROBABILITY`,
`KNIGHTS_RATING`, `KNIGHTS_ALLIANCE`, `KNIGHTS_CAPE`, `KNIGHTS_SIEGE_WARFARE`,
`KNIGHTS_USER`, `COUPON_SERIAL_LIST`, `ITEM_UPGRADE`, `ITEMUP_PROBABILITY`,
`ITEM_EXCHANGE`, `HOME`, `COEFFICIENT`, `EVENT_TRIGGER`, `SERVER_RESOURCE`,
`START_POSITION`, `USER_PERSONAL_RANK`, `USER_KNIGHTS_RANK`, `WEBPAGE_ADDRESS`,
`KING_SYSTEM`, `KING_ELECTION_LIST`, `KING_CANDIDACY_NOTICE_BOARD`.

## Neue Event-Script-Opcodes (Quest-VM, `EVENT.cpp`/`LOGIC_ELSE.cpp`/`EXEC.cpp`)

`CHECK_EXIST_ITEM_AND/OR`, `CHECK_NOEXIST_ITEM_AND/OR`, `CHECK_INPUT_COUNT`,
`CHECK_EXIST_ITEM_INPUT_COUNT`, `CHECK_WEIGHT_INPUT_COUNT`, `GIVE_ITEM_INPUT_COUNT`,
`CHECK_MANNER`, `CHECK_LOYALTY_RANK(_MONTHLY)`, `CHECK_EXAM_COUNT`, `CHECK_CLAN/NO_CLAN`,
`CHECK_EXCHANGE`, `CHECK_MONSTER_CHALLENGE_TIME/USERCOUNT`, `CHECK_PCBANG_ITEM/OWNER`,
`CHECK_PPCARD_SERIAL/TYPE`, `CHECK_PROMOTION_ELIGIBLE`, `CHECK_KJWAR/OLYMPIC/LOGTIME_ACCOUNT`,
`GIVE_KJWAR/LOGTIME/PCBANG_ITEM`, `CHECK_BEEF_ROAST_*`, `CHECK_MIDDLE_STATUE_*`.

## Neue GM-Kommandos (Auszug)

`/challenge_on|_off|_stop|_kill|_level` (Monster Challenge), `/rental_start|_stop|_report`,
`/reload_king`, `/reload_hacktool`, `/reload_notice`, `/discount|/alldiscount|/freediscount`,
`/exp_add`, `/money_add`, `/santa|/offsanta`, `/snowopen`, `/onsummonblock|/offsummonblock`,
`/server_testmode|_normalmode`, `/tiebreak`, `/limitbattle`, `+forbiduser`, `+forbidconnect`.

## Neue Config-Sektionen (`.ini.default`)

`[MONSTER_CHALLENGE] ACTIVATE/LEVEL`, `[BONUS_EVENT] MONEY/EXP`, `[MATURE_SETTING] PK_PERMIT`,
`[PREMIUM_ITEM] ITEM_NUM1/2` (Aujard), `[BILLING] IP/PORT/NUM` (Aujard).

## Umsetzungs-Reihenfolge (Ziel-Dateien im Port)

**B1 zuerst** (verifizierbar): DB-Proc-Aufruf → `dotnet/src/Servers/OpenKO.Servers.Aujard/{IDbAgent,DbAgent}.cs`
(`{call …}`-Signatur ist **VERIFIED** aus den Exe-Strings); Paket-Handler →
`dotnet/src/Servers/OpenKO.Servers.Ebenezer/GameUser.*.cs` (+ Opcode-Enum in
`OpenKO.Core/Protocol/*`); Event-Opcodes → Quest-VM; Config → Options-Klassen.
Empfohlene Startreihenfolge nach Aufwand/Isolation: **Skill-Shortcut → Saved-Magic →
Friend-List → Knights/Siege → King-System**.

**B2 danach** (China/Kommerz): Handler + Proc-Aufruf implementieren, Proc-Body als
`// TODO(re): proc body unknown` offen dokumentieren; end-to-end lauffähig erst mit
RoK-Schema oder .NET-seitiger Nachbildung.

## Verifikation

- `dotnet build|test dotnet/OpenKO.slnx -c Release` — bestehende Tests dürfen nicht brechen.
- Pro Proc: erst die **laufende Ziel-DB** abfragen (`sys.procedures`), ob sie existiert;
  dann Round-trip-Test (`OPENKO_TEST_DB`).
- Pro Handler: zugehörige Exe-Region per Ghidra/`objdump` gegenlesen (Feldreihenfolge/Konstanten).
