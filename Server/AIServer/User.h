// User.h: interface for the CUser class.
//
//////////////////////////////////////////////////////////////////////

#if !defined(AFX_USER_H__1CC8CB68_CF95_4849_8E89_134826B1FAC2__INCLUDED_)
#define AFX_USER_H__1CC8CB68_CF95_4849_8E89_134826B1FAC2__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#include "iocport.h"
#include "MagicProcess.h"
class CServerDlg;

#include "extern.h"
#include <shared/STLMap.h>
#include <list>

typedef std::list <_USERLOG*>		UserLogList;

class CUser
{
public:
	CServerDlg* m_pMain;

	CIOCPort* m_pIocport;
	CMagicProcess m_MagicProcess;

	UserLogList	m_UserLogList;

	// 필요한 정보 변수만 선언,,, Variable
	// 캐릭터 기본 속성 Character
	char			m_strUserID[MAX_ID_SIZE + 1];	// 캐릭터의 이름 Character
	int				m_iUserId;			// User의 번호 [Korean comment]
	BYTE			m_bLive;			// 죽었니? 살았니? [Korean comment]

	float			m_curx;				// 현재 X 좌표
	float			m_cury;				// 현재 Y 좌표
	float			m_curz;				// 현재 Z 좌표
	float			m_fWill_x;			// 다음 X 좌표
	float			m_fWill_y;			// 다음 Y 좌표
	float			m_fWill_z;			// 다음 Z 좌표
	short			m_sSpeed;			// 유저의 스피드	 [Korean comment]
	BYTE 			m_curZone;			// 현재 존 [Korean comment]
	short			m_sZoneIndex;		// 현재 존의 index 번호..

	BYTE	m_bNation;						// 소속국가 [Korean comment]
	short	m_sLevel;						// 레벨 [Korean comment]

	short	m_sHP;							// HP
	short	m_sMP;							// MP
	short	m_sSP;							// SP
	short	m_sMaxHP;						// HP
	short	m_sMaxMP;						// MP
	short	m_sMaxSP;						// SP

	BYTE	m_state;						// User의 상태 Status

	short	m_sRegionX;						// 현재 영역 X 좌표
	short	m_sRegionZ;						// 현재 영역 Z 좌표
	short	m_sOldRegionX;					// 이전 영역 X 좌표
	short	m_sOldRegionZ;					// 이전 영역 Z 좌표

	BYTE	m_bResHp;						// 회복량 [Korean comment]
	BYTE	m_bResMp;
	BYTE	m_bResSta;

	BYTE    m_byNowParty;				// 파티중이면 1, 부대중이면 2, 둘다 아니면 0 [Korean comment]
	BYTE	m_byPartyTotalMan;			// 파티 맺은 총 구성 인원수  [Korean comment]
	short   m_sPartyTotalLevel;			// 파티 맺은 사람의 총 레벨 [Korean comment]
	short	m_sPartyNumber;				// 파티 번호 [Korean comment]

	short	m_sHitDamage;				// Hit
	float	m_fHitrate;					// 공격 민첩률 [Korean comment]
	float	m_fAvoidrate;				// 방어 민첩률 [Korean comment]
	short	m_sAC;						// 방어율 [Korean comment]
	short   m_sItemAC;                  // 아이템 방어률 [Korean comment]


	short  m_sSurroundNpcNumber[8];		// Npc 다굴~ [Korean comment]

	BYTE   m_byIsOP;					// 운영자인지를 판단.. [Korean comment]
	long   m_lUsed;						// 포인터 사용유무.. (1:사용중.. 접근 허락치 않음. 0:사용해도 무방) [Korean comment]

	BOOL		m_bLogOut;				// Logout 중인가? [Korean comment]

	// 2002.7.10 - Yookozuna
	BYTE    m_bMagicTypeLeftHand;			// The type of magic item in user's left hand  
	BYTE    m_bMagicTypeRightHand;			// The type of magic item in user's right hand
	short   m_sMagicAmountLeftHand;         // The amount of magic item in user's left hand
	short	m_sMagicAmountRightHand;        // The amount of magic item in user's left hand

public:
	void InitUserLog();
	void WriteUserLog();
	short GetMagicDamage(int damage, short tid);
	void Initialize();
	void InitNpcAttack();
	void Attack(int sid, int tid);	// ATTACK
	void SetDamage(int damage, int tid);				// user damage
	void Dead(int tid, int nDamage);					// user dead
	void SetExp(int iNpcExp, int iLoyalty, int iLevel);		// user exp
	void SetPartyExp(int iNpcExp, int iLoyalty, int iPartyLevel, int iMan);		// user exp
	short GetDamage(int tid, int magicid = 0);
	BYTE GetHitRate(float rate);
	int IsSurroundCheck(float fX, float fY, float fZ, int NpcID);
	void HealMagic();
	void HealAreaCheck(int rx, int rz);

	void SendAttackSuccess(int tuid, BYTE result, short sDamage, int nHP = 0, short sAttack_type = 1);  // 공격 성공 [Korean comment]
	void SendMagicAttackResult(int tuid, BYTE result, short sDamage, short sHP = 0);  // 공격 성공 [Korean comment]
	void SendHP();												// user의 HP
	void SendExp(int iExp, int iLoyalty, int tType = 1);
	void SendSystemMsg(const std::string_view msg, BYTE type, int nWho);
	void SendAll(const char* pBuf, int nLength);						// game server로 패킷 전송... [Korean comment]
	BOOL IsOpIDCheck(char* szName);

	CUser();
	virtual ~CUser();
};

#endif // !defined(AFX_USER_H__1CC8CB68_CF95_4849_8E89_134826B1FAC2__INCLUDED_)
