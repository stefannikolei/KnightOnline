#include "StdAfx.h"
#include "EbenezerDlg.h"

#include <shared/ServerResourceFormatter.h>

bool fmt::resource_helper::get_from_db(uint32_t resourceId, std::string& fmtStr)
{
	CEbenezerDlg* instance = CEbenezerDlg::GetInstance();
	if (instance == nullptr)
		return false;

	model::ServerResource* serverResource = instance->m_ServerResourceTableMap.GetData(resourceId);
	if (serverResource == nullptr)
		return false;

	fmtStr = serverResource->Resource;
	return true;
}
