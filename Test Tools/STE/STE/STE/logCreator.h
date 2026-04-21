#include <string>

#ifndef LOGCREATOR_H_INCLUDED
#define LOGCREATOR_H_INCLUDED

/* Creates new log file.

	Precondition:
		testname is the file name of the test currently running.

	Postcondition:
		A new html file is created with the name [CURRENT_TEST_NAME]-[DATE]-[TIME].

*/
void createNewLog(const std::string testName);

/* Returns the name of the log file to be created.

	Precondition:
		testname is the file name of the test currently running.

	Postcondition:
		fileName is returned successfully.

*/
std::string formatFileName(const std::string testName);

/* Appends log message to the log file.
	Precondition:
		fileName is the file name of the log corresponding to the test currently
				running.
		logMessage is the message to be logged.

	Postcondition:
		logMessage is appended to the log file successfully.
*/
void appendLog(const std::string fileName, const std::string logMessage);

#endif
