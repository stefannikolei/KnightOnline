#pragma once
#include <string>
#include <memory>

// forward declarations
class CIni;
namespace spdlog::details
{
	class thread_pool;
}

/// \brief Sets up spdlog from an ini file using standardized server settings
/// \param ini server application's ini file (already loaded)
/// \param name application name (VersionManager, Aujard, AIServer, Ebenezer)
void SetupLogger(CIni& ini, const std::string& name);

void SetupExtraLogger(CIni& ini, const std::string& appName,
	const std::string& logFileConfigProp,
	std::shared_ptr<spdlog::details::thread_pool> threadPool);

namespace ini
{
	// LOGGER section
	static constexpr char LOGGER[] = "LOGGER";
	static constexpr char LEVEL[] = "LEVEL";
	static constexpr char PATTERN[] = "PATTERN";
	static constexpr char FILE[] = "FILE";
	static constexpr char ITEM_LOG_FILE[] = "ITEM_LOG_FILE";
	static constexpr char USER_LOG_FILE[] = "USER_LOG_FILE";
	static constexpr char REGION_LOG_FILE[] = "REGION_LOG_FILE";
	static constexpr char EVENT_LOG_FILE[] = "EVENT_LOG_FILE";

	/// \brief default logger line prefix ([12:59:59][AppName][  level] log line...)
	static constexpr char DEFAULT_LOG_PATTERN[] = "[%H:%M:%S][%n][%7l] %v";
}

/// \brief contains the application names used by our loggers
namespace logger
{
	static constexpr char AIServer[] = "AIServer";
	static constexpr char AIServerItem[] = "AIServerItem";
	static constexpr char AIServerUser[] = "AIServerUser";
	static constexpr char Ebenezer[] = "Ebenezer";
	static constexpr char EbenezerEvent[] = "EbenezerEvent";
	static constexpr char EbenezerRegion[] = "EbenezerRegion";
	static constexpr char Aujard[] = "Aujard";
	static constexpr char VersionManager[] = "VersionManager";
}
