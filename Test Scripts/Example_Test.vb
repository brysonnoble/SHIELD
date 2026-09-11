Imports System

Module Example_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
            RunTestCase(AddressOf TC03)
        Finally
            EndTest()
        End Try
    End Sub

    ' RunTestCase() calls TestCaseBegin()/TestCaseEnd() around this and
    ' records PASS/FAIL for EndTest()'s summary - a TCxx() only needs to
    ' TraceTo() and assert.
    Sub TC01()
        TraceTo("REQ_NAME")
        ' Test Case 01
    End Sub
    Sub TC02()
        TraceTo("REQ_NAME")
        ' Test Case 02
    End Sub
    Sub TC03()
        TraceTo("REQ_NAME")
        ' Test Case 03
    End Sub
End Module
