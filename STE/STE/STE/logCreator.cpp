#include "logCreator.h"

#include <ctime>
#include <fstream>
#include <iostream>
#include <string>

using namespace std;

// Creates new log file.
void createNewLog(const string testName)
{
	const string fileName = formatFileName(testName);

	ofstream logFile(fileName,ios::app);
}

// Returns the name of the log file to be created.
string formatFileName(const string testName)
{
	time_t timestamp = time(NULL);
	struct tm datetime;
	localtime_s(&datetime, &timestamp);

	char fileDateTime[50];

	strftime(fileDateTime, 50, "%m%d%y - %I%M%S", &datetime);

	const string fileName = testName + " - " + fileDateTime + ".html";

	return fileName;
}

// Appends log message to the log file.
void appendLog(const string fileName, const string logMessage)
{
	ofstream logFile(fileName,ios::app);

	logFile << logMessage << endl;
}
