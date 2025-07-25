#pragma once
#include <string>

// forward declarations
class CIni;

/// \brief Sets up spdlog from an ini file using standardized server settings
/// \param ini server application's ini file (already loaded)
/// \param appName application name (VersionManager, Aujard, AIServer, Ebenezer)
void SetupLogger(CIni& ini, const std::string& appName);

namespace ini
{
	// LOGGER section
	static constexpr char LOGGER[] = "LOGGER";
	static constexpr char LEVEL[] = "LEVEL";
	static constexpr char PATTERN[] = "PATTERN";
	static constexpr char FILE[] = "FILE";

	/// \brief default logger line prefix ([12:59:59][AppName][  level] log line...)
	static constexpr char DEFAULT_LOG_PATTERN[] = "[%H:%M:%S][%n][%7l] %v";
}
