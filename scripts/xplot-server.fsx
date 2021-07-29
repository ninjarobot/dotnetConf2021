#r "nuget: XPlot.Plotly"
#r "nuget: Suave"

open XPlot.Plotly
open Suave
open System

let generateChart (chartTitle:string) =
    let z =
        let rnd = Random()
        [ for _ in 1 .. 25 ->
            [ for _ in 1 .. 25 ->
                rnd.Next(0, 400)
            ]
        ]

    let layout =
        Layout(
            autosize = false,
            margin =
                Margin(
                    l = 65.,
                    r = 50.,
                    b = 65.,
                    t = 90.
                ),
            title = chartTitle
        )

    let chart =
        Surface(z = z)
        |> Chart.Plot
        |> Chart.WithLayout layout
        |> Chart.WithWidth 700
        |> Chart.WithHeight 500
    
    chart.GetHtml()

let httpChart : WebPart =
    fun (ctx:HttpContext) ->
        let chartHtml = generateChart "Doing mathy stuff with a 3D surface chart"
        Successful.OK chartHtml ctx

let config = { defaultConfig with bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" 80 ] }
startWebServer config httpChart
