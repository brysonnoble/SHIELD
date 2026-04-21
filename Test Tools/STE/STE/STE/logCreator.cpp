#include "logCreator.h"

#include <ctime>
#include <fstream>
#include <iostream>
#include <string>

// Creates new log file.
void createNewLog(const std::string testName)
{
	const std::string fileName = formatFileName(testName);

	std::ofstream logFile(fileName, std::ios::app);
}

// Returns the name of the log file to be created.
std::string formatFileName(const std::string testName)
{
	time_t timestamp = time(NULL);
	struct tm datetime;
	localtime_s(&datetime, &timestamp);

	char fileDateTime[50];

	strftime(fileDateTime, 50, "%m%d%y - %I%M%S", &datetime);

	const std::string fileName = testName + " - " + fileDateTime + ".html";

	return fileName;
}

// Appends log message to the log file.
void appendLog(const std::string fileName, const std::string logMessage)
{
	std::ofstream logFile(fileName, std::ios::app);

	logFile << logMessage << std::endl;
}
