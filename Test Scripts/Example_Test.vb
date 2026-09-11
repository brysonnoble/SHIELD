Imports System

Module Example_Test
    Sub Main()
        BeginTest()

        TC01()
        TC02()
        TC03()

        EndTest()
    End Sub

    Sub TC01()
        TestCaseBegin()
        TraceTo("REQ_NAME")
        ' Test Case 01
        TestCaseEnd()
    End Sub
    Sub TC02()
        TestCaseBegin()
        TraceTo("REQ_NAME")
        ' Test Case 02
        TestCaseEnd()
    End Sub
    Sub TC03()
        TestCaseBegin()
        TraceTo("REQ_NAME")
        ' Test Case 03
        TestCaseEnd()
    End Sub
End Module
