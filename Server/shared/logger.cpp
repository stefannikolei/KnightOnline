#include "stdafx.h"
#include "logger.h"
#include "Ini.h"

#include <spdlog/spdlog.h>
#include <spdlog/sinks/daily_file_sink.h>
#include <spdlog/common.h>
#include <spdlog/sinks/stdout_color_sinks.h>
#include <spdlog/async.h>
#include <spdlog/async_logger.h>

/// \brief Sets up spdlog from an ini file using standardized server settings
/// \param ini server application's ini file (already loaded)
/// \param appName application name (VersionManager, Aujard, AIServer, Ebenezer)
void SetupLogger(CIni& ini, const std::string& appName)
{
	// setup file logger
	std::string defaultLogPath = std::format("{}.log", appName);
	std::string fileName = ini.GetString(ini::LOGGER, ini::FILE, defaultLogPath);
	auto fileLogger = std::make_shared<spdlog::sinks::daily_file_format_sink_mt>(fileName, 0, 0);

	// setup console logger
	auto consoleLogger = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();

	// setup multi-sink async logger as default (combines file+console logger)
	spdlog::init_thread_pool(8192, 1);
	std::vector<spdlog::sink_ptr> sinks{fileLogger, consoleLogger};
	std::shared_ptr<spdlog::async_logger> appLogger = std::make_shared<spdlog::async_logger>(
		appName, sinks.begin(), sinks.end(), spdlog::thread_pool(), spdlog::async_overflow_policy::block);
	spdlog::set_default_logger(appLogger);

	// set default logger level and pattern
	// we default to debug level logging
	int logLevel = ini.GetInt(ini::LOGGER, ini::LEVEL, spdlog::level::debug);
	spdlog::set_level(static_cast<spdlog::level::level_enum>(logLevel));
	std::string logPattern = ini.GetString(ini::LOGGER, ini::PATTERN, ini::DEFAULT_LOG_PATTERN);
	spdlog::set_pattern(logPattern);

	// periodically flush all *registered* loggers every 3 seconds:
	// warning: only use if all your loggers are thread-safe ("_mt" loggers)
	spdlog::flush_every(std::chrono::seconds(3));

	spdlog::info("{} logger configured", appName);
}
