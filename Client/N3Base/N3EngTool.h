// N3EngTool.h: interface for the CN3EngTool class.
//
//////////////////////////////////////////////////////////////////////

#if !defined(AFX_D3DENGINETEST_H__4DE7DD27_A9BC_43C5_9D67_E99031ED38B5__INCLUDED_)
#define AFX_D3DENGINETEST_H__4DE7DD27_A9BC_43C5_9D67_E99031ED38B5__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#include "N3Eng.h"

struct __EXPORT_OPTION
{
	char szID[8];		// ID "N3Scene1"
	char szRemark[64];	// 설명.. // Description..
	
	int nNodeCount; // 전체 노드 카운트 // Total node count
	
	BOOL	bAnimationKey; 
	int		nFrmStart;	// 에니메이션 시작 프레임  // Animation start frame
	int		nFrmEnd;	// 에니메이션 끝 프레임 // Animation end frame
	float	fSamplingRate; // Key Sampling - 30.0f 가 표준.. // Key Sampling - 30.0f is standard..

	D3DCOLORVALUE dcvBackground;	// 배경 색 // Background color
	D3DCOLORVALUE dcvAmbientLight;	// 기본 조명 색 // Basic lighting color

//	int nCameraCount;	// scene 내의 카메라 갯수 // Number of cameras in scene
//	int nMaterialCount;	// scene 내의 재질 갯수 // Number of materials in scene
//	int nTextureCount;	// scene 내의 텍스처 갯수 // Number of textures in scene
//	int nLightCount;	// scene 내의 조명 갯수 // Number of lights in scene

	BOOL bExportCamera;	// 카메라 데이터를 갖고 있다. // Has camera data.
	BOOL bExportLight;		// 라이트 데이터를 갖고 있다. // Has light data.
	BOOL bExportGeometry;	// 지오메트리 데이터를 갖고 있다. // Has geometry data.
	BOOL bExportDummy;	// 도우미 오브젝트(??? - 실제 겜에서는 필요없고 개발시에만 필요한 오브젝트를 말한다)데이터를 갖고 있다.  // Has dummy object data (helper objects not needed in actual game, only during development).
	BOOL bExportCharacter;	// 도우미 오브젝트(??? - 실제 겜에서는 필요없고 개발시에만 필요한 오브젝트를 말한다)데이터를 갖고 있다.  // Has character data (helper objects not needed in actual game, only during development).
	
	BOOL bExportSelectedOnly; // 선택된 것만 ??? // Export selected only ???

	BOOL bGenerateFileName; // 파일 이름을 0_0000_00_0 포맷으로 바꾼다..?? // Change file name to 0_0000_00_0 format..??
	BOOL bGenerateSmoothNormal; // 부드럽게 보이도록 법선 벡터들을 재 계산한다. // Recalculate normal vectors for smooth appearance.
//	BOOL bGenerateProgressiveMesh; // Progressive Mesh 생성 // Progressive Mesh generation
	BOOL bGenerateHalfSizeTexture; // 텍스처 파일을 자동으로 최적화 시켜서 생성 Direct3D 의 포맷에 맞게 2의 제곱수 단위로 맞추어서 "OBM" 비트맵 파일로 저장. // Automatically optimize texture files and save as "OBM" bitmap files in power-of-2 units to match Direct3D format.
	BOOL bGenerateCompressedTexture; // Texture 압축 사용 // Use texture compression

	char szSubDir[_MAX_DIR];		// export 할때 저장하는 sub폴더를 지정해준다. // Specify subfolder to save when exporting.
};

class CN3EngTool : public CN3Eng  
{
public:
	int					m_nGridLineCount; // 그리드 라인 카운트.. // Grid line count..
	__VertexColor*		m_pVGrids; // 그리드 렌더링 용 // For grid rendering

	__VertexColor		m_VAxis[60]; // 축 렌더링 용 // For axis rendering
	__VertexColor		m_VDir[6]; // 방향 표시 용 // For direction display
	__VertexTransformed m_VPreview[6];	// 텍스처 프리뷰 용 // For texture preview



//	LPDIRECT3DDEVICE8	m_lpD3DDevExtra;

public:
//	bool CreateExtraDevice(int nWidth, int nHeight);
	void GridCreate(int nWidth, int nHeight);
	void RenderTexturePreview(CN3Texture* pTex, HWND hDlgWndDiffuse, RECT* pRCSrc = nullptr);
	void RenderGrid(const __Matrix44& mtxWorld);
	void RenderAxis(bool bShowDir = false);

	CN3EngTool();
	virtual ~CN3EngTool();

};

#endif // !defined(AFX_D3DENGINETEST_H__4DE7DD27_A9BC_43C5_9D67_E99031ED38B5__INCLUDED_)
