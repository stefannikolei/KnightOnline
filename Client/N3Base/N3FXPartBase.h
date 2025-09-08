// N3FXPartBase.h: interface for the CN3FXPartBase class.
//
//////////////////////////////////////////////////////////////////////

#if !defined(AFX_N3EFFECTPARTBASE_H__E11B5E1E_C4FF_4186_9645_E05DB129747B__INCLUDED_)
#define AFX_N3EFFECTPARTBASE_H__E11B5E1E_C4FF_4186_9645_E05DB129747B__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#include "N3BaseFileAccess.h"
#include "N3FXDef.h"
#include "N3Texture.h"

class CN3FXBundle;
class CN3FXPartBase : public CN3BaseFileAccess  
{
public:
	static constexpr int SUPPORTED_PART_BASE_VERSION = 4; // supported as far as reading only

//멤버 변수들.. Variable
	int				m_iVersion;			//	자료의 버전.. [Korean comment]
	int				m_iBaseVersion;

	CN3FXBundle*	m_pRefBundle;		//	내가 속한 번들의 포인터. [Korean comment]
	CN3FXPartBase*	m_pRefPrevPart;		//	내 앞의 파트.. [Korean comment]

	int				m_iType;			//	형식타입(particle, mesh, board...)
	float			m_fLife;			//	플레이시간.. Time

	std::string		m_strName;			//	파트이름.. Name

	__Vector3		m_vVelocity;		//	초당 움직이는 속도..(초속도) [Korean comment]
	__Vector3		m_vAcceleration;	//	가속도...(중력가속도를 넣고 싶으면 y = -9.8..^^
	__Vector3		m_vRotVelocity;		//	초당 회전할 수 있는 각속도.. Rotate

	float			m_fCurrLife;			
	__Vector3		m_vCurrVelocity;
	__Vector3		m_vCurrPos;

	uint32_t		m_dwState;			//	현재 파트의 상태.. Status
	__Vector3		m_vPos;				//	번들에서 파트의 위치. Position

	bool			m_bOnGround;		//바닥에 붙어서 갈 것인가... [Korean comment]

	//texture..
	char			m_pTexName[MAX_PATH];
	CN3Texture**	m_ppRefTex;
	int				m_iNumTex;
	float			m_fTexFPS;

	float			m_fFadeOut;
	float			m_fFadeIn;

	uint32_t		m_dwRenderFlag;

	uint32_t		m_dwSrcBlend;
	uint32_t		m_dwDestBlend;
	BOOL			m_bAlpha;

	uint32_t		m_dwZEnable;
	uint32_t		m_dwZWrite;
	uint32_t		m_dwLight;
	uint32_t		m_dwDoubleSide;
		
protected:
	virtual bool	IsDead();

public:
	virtual float	GetGroundHeight(float x, float z) { return 0.01f; }

	virtual void	Init();				//	각종 변수들을 처음 로딩한 상태로 초기화... Initialize
	virtual void	Start();			//	파트 구동 시작. [Korean comment]
	virtual void	Stop();				//	파트 구동 멈춤.. [Korean comment]
	virtual bool	Tick();				//	ticktick...
	virtual void	Render();			//	화면에 뿌리기.. [Korean comment]
	virtual bool	Load(HANDLE hFile);	//	게임파일 불러오기. File
	virtual bool	Save(HANDLE hFile);	//	게임파일 저장하기. Save
	void	Duplicate(CN3FXPartBase* pSrc);

#ifdef _N3TOOL
protected:
	virtual bool	ParseScript(char* szCommand, char* szBuff0, char* szBuff1, char* szBuff2, char* szBuff3);	//실질적인 스크립트 해석 함수.. Function
	
public:
	bool	DecodeScriptFile(const char* lpPathName);
#endif // end of _N3TOOL

public:
//생성자 소멸자.. Constructor
	CN3FXPartBase();
	virtual ~CN3FXPartBase();
};

#endif // !defined(AFX_N3EFFECTPARTBASE_H__E11B5E1E_C4FF_4186_9645_E05DB129747B__INCLUDED_)
