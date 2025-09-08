// N3FXPartMesh.h: interface for the CN3FXPartMesh class.
//
//////////////////////////////////////////////////////////////////////

#ifndef __N3FXPARTMESH_H__
#define __N3FXPARTMESH_H__

#include "N3FXPartBase.h"

class CN3FXShape;
class CN3FXPartMesh : public CN3FXPartBase
{
public:
	static constexpr int SUPPORTED_PART_VERSION = 9; // supported as far as reading only

	CN3FXShape*	m_pShape;
	CN3FXShape*	m_pRefShape;

	uint32_t		m_dwCurrColor;	//
	
	char		m_cTextureMoveDir;	//텍스쳐 이동 방향..1:up 2:down, 3:left, 4:right
	float		m_fu;				//텍스쳐 이동 속도 Move
	float		m_fv;				//텍스쳐 이동 속도 Move

	__Vector3	m_vUnitScale;
	__Vector3	m_vScaleVel;
	__Vector3	m_vCurrScaleVel;
	__Vector3	m_vScaleAccel;
	__Vector3	m_vDir;

	bool		m_bTexLoop;
	float		m_fMeshFPS;

	// N3FXPartMesh needs implementation of these methods
	bool m_bShapeLoop;
	bool m_bViewFix;
	bool m_bUseFadeShowLife;
	// N3FXPartMesh needs implementation of these methods
protected:
	bool	IsDead();

public:
	void	Init();				//	각종 변수들을 처음 로딩한 상태로 초기화... Initialize
	void	Start();			//	파트 구동 시작. [Korean comment]
	void	Stop();				//	파트 구동 멈춤.. [Korean comment]
	bool	Tick();				//	ticktick...
	void	Render();			//	화면에 뿌리기.. [Korean comment]
	bool	Load(HANDLE hFile);	//	게임파일 불러오기. File
	bool	Save(HANDLE hFile);	//	게임파일 저장오기. Save
	void	Duplicate(CN3FXPartMesh* pSrc);
		
public:
	void	Rotate();
	void	Move();
	void	Scaling();
	void	MoveTexUV();

	int		NumPart();
	int		NumVertices(int Part);
	LPDIRECT3DVERTEXBUFFER9 GetVB(int Part);

	CN3FXPartMesh();
	virtual ~CN3FXPartMesh();

#ifdef _N3TOOL
	bool	ParseScript(char* szCommand, char* szBuff0, char* szBuff1, char* szBuff2, char* szBuff3);
#endif // end of _N3TOOL

};

#endif // #ifndef __N3FXPARTMESH_H__
